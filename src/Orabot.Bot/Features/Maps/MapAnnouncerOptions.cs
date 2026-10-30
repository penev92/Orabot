namespace Orabot.Bot.Features.Maps
{
	public sealed class MapAnnouncerOptions
	{
		public const string SectionName = "MapAnnouncer";
		public string ServerName { get; set; }
		public string ChannelName { get; set; }
		public int ScanInterval { get; set; } = 60000;
	}
}
