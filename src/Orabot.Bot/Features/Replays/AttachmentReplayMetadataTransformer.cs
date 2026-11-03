using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Replays.Models;
using Orabot.Bot.Infrastructure.Attachments;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Orabot.Bot.Features.Replays
{
	internal sealed class AttachmentReplayMetadataTransformer
	{
		internal const int MaximumReplayBytes = 64 * 1024 * 1024;
		internal const int MaximumMetadataBytes = 1024 * 1024;
		internal const int MaximumPlayers = 64;
		private const int MetaStartMarker = -1;
		private const int MetaEndMarker = -2;
		private const int MetaVersion = 1;
		private static readonly Regex Sections = new(@"^(?<key>[A-Za-z][A-Za-z0-9_]*(?:@[0-9]+)?):[ \t]*\r?$",
			RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
		private readonly AttachmentDownloader _downloader;
		private readonly string _storageFolder;
		private readonly IDeserializer _yaml;

		public AttachmentReplayMetadataTransformer(AttachmentDownloader downloader, IOptions<ReplayOptions> options, IDeserializer yamlDeserializer)
		{
			_downloader = downloader;
			_storageFolder = options.Value.ReplayStorageFolder;
			_yaml = yamlDeserializer;
		}

		internal async Task<ReplayMetadata> GetMetadataAsync(IAttachment attachment, CancellationToken cancellationToken = default)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(AttachmentDownloader.OperationTimeout);
			var archive = await _downloader.DownloadAsync(attachment, _storageFolder, MaximumReplayBytes, ".orarep", deadline.Token);
			await using var stream = new FileStream(archive.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
				81920, FileOptions.Asynchronous | FileOptions.RandomAccess);
			return await ParseAsync(stream, attachment.Filename, deadline.Token);
		}

		internal async Task<ReplayMetadata> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
		{
			var miniYaml = await ReadMetadataAsync(stream, cancellationToken);
			var yaml = miniYaml.Replace("\t", "  ").Replace("{DEV_VERSION}", "DEV_VERSION");
			try
			{
				ValidateYamlBudget(yaml, cancellationToken);
				var sections = Sections.Matches(yaml);
				if (sections.Count == 0 || sections.Count > MaximumPlayers + 16)
					throw InvalidMetadata();
				Root root = null;
				var players = new Dictionary<int, PlayerData>();
				for (var index = 0; index < sections.Count; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var section = sections[index];
					var start = section.Index + section.Length;
					var end = index + 1 < sections.Count ? sections[index + 1].Index : yaml.Length;
					var body = yaml.Substring(start, end - start);
					var key = section.Groups["key"].Value;
					if (key == "Root")
					{
						if (root != null)
							throw InvalidMetadata();
						root = _yaml.Deserialize<Root>(body);
					}
					else if (key == "Player" || key.StartsWith("Player@", StringComparison.Ordinal))
					{
						if (players.Count >= MaximumPlayers)
							throw InvalidMetadata();
						var player = _yaml.Deserialize<PlayerData>(body);
						if (player == null || !Bounded(player.Name, 256) || !Bounded(player.FactionName, 256)
							|| !Bounded(player.DisplayFactionName, 256) || !Bounded(player.Outcome, 64, optional: true)
							|| !players.TryAdd(player.ClientIndex, player))
							throw InvalidMetadata();
					}
				}
				if (root == null || !Bounded(root.Mod, 64) || !Bounded(root.Version, 128)
					|| !Bounded(root.MapUid, 128) || !Bounded(root.MapTitle, 512)
					|| root.FinalGameTick < 0 || root.EndTimeUtc < root.StartTimeUtc)
					throw InvalidMetadata();
				return new ReplayMetadata
				{
					FileName = fileName, Mod = root.Mod, Version = root.Version, MapUid = root.MapUid,
					MapTitle = root.MapTitle, FinalGameTick = root.FinalGameTick,
					StartTimeUtc = root.StartTimeUtc, EndTimeUtc = root.EndTimeUtc, Players = players
				};
			}
			catch (Exception exception) when (exception is YamlException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
			{
				// Parser diagnostics can contain uploaded content. Expose a stable category only.
				throw InvalidMetadata();
			}
		}

		internal static async Task<string> ReadMetadataAsync(Stream stream, CancellationToken cancellationToken = default)
		{
			if (!stream.CanSeek || stream.Length < 20 || stream.Length > MaximumReplayBytes)
				throw InvalidMetadata();
			var footer = new byte[8];
			stream.Seek(-8, SeekOrigin.End);
			await stream.ReadExactlyAsync(footer, cancellationToken);
			var length = BinaryPrimitives.ReadInt32LittleEndian(footer);
			if (BinaryPrimitives.ReadInt32LittleEndian(footer.AsSpan(4)) != MetaEndMarker
				|| length < 4 || length > MaximumMetadataBytes + 4)
				throw InvalidMetadata();
			var start = stream.Length - 16L - length;
			if (start < 0)
				throw InvalidMetadata();
			stream.Seek(start, SeekOrigin.Begin);
			var header = new byte[12];
			await stream.ReadExactlyAsync(header, cancellationToken);
			var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
			if (BinaryPrimitives.ReadInt32LittleEndian(header) != MetaStartMarker
				|| BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) != MetaVersion
				|| payloadLength <= 0 || payloadLength != length - 4)
				throw InvalidMetadata();
			var payload = new byte[payloadLength];
			await stream.ReadExactlyAsync(payload, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			try { return new UTF8Encoding(false, true).GetString(payload); }
			catch (DecoderFallbackException) { throw InvalidMetadata(); }
		}

		private static void ValidateYamlBudget(string yaml, CancellationToken cancellationToken)
		{
			var parser = new Parser(new StringReader(yaml));
			var depth = 0;
			var events = 0;
			while (parser.MoveNext())
			{
				cancellationToken.ThrowIfCancellationRequested();
				var current = parser.Current;
				depth += current.NestingIncrease;
				if (++events > 20000 || depth > 32 || current is AnchorAlias
					|| (current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
					|| (current is Scalar scalar && scalar.Value.Length > 8192))
					throw InvalidMetadata();
			}
		}

		private static bool Bounded(string value, int maximum, bool optional = false) =>
			optional && value == null || !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

		private static InvalidDataException InvalidMetadata() => new("The replay metadata is corrupt, unsupported or exceeds its processing budget.");
	}
}
