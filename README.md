# Orabot

Orabot is an OpenRA community Discord bot, hosted as a Windows console application.

## Structure

The application is a single-process modular monolith organized into vertical feature slices.

```text
Orabot.slnx
src/
  Orabot.Hosts.ConsoleHost/   Console entry point, configuration and host lifetime
  Orabot.Bot/
    Features/
      Documentation/        Documentation commands, autocomplete, cache and rendering
      General/              General commands, information and mod-timer responses
      GitHubReferences/     Issue and pull-request references
      Logs/                 Log attachments and stack traces
      Maps/                 Map references, previews and announcements
      Quotes/               Message and custom quotations
      Replays/              Replay attachments, metadata and rendering
      Roles/                Self-assigned and reaction-assigned roles
    Discord/                Connection lifecycle, dispatch and shared Discord contracts
    Infrastructure/         Configuration validation, worker supervision and serialization
    DependencyInjection/    Bot service registration
tests/
  Orabot.Tests/             Offline configuration, lifecycle and worker tests
```

Feature-specific handlers, models and rendering live together. Shared Discord dispatch contracts are in the bot project; the former `Orabot.Core.Abstractions` project has been absorbed into it.

The current feature relationships are preserved:

- General help uses the Quotes module name to group quote commands.
- Replay rendering uses the Maps renderer to include map information.
- Logs and Replays share the attachment-handler base in `Discord/Messages`.

New feature code belongs with its owning slice. Shared infrastructure supports multiple features. Further separation of input handling, external I/O and rendering can happen within each feature's modernization.

## Build and test

The projects target **.NET 10** and use **Discord.Net 3.20.2**. Use a .NET 10 SDK to build the solution. Running the framework-dependent console output requires the .NET 10 runtime.

From the repository root:

```powershell
dotnet restore Orabot.slnx
dotnet build Orabot.slnx --configuration Release --no-restore
dotnet test Orabot.slnx --configuration Release --no-build --no-restore
```

Package references and versions are declared in the project files. NuGet auditing includes transitive dependencies.

Tests use synthetic configuration, fake APIs, fake time and an offline connection seam. They do not log into Discord or read the operator's User Secrets store.

Build output for the executable is under `src/Orabot.Hosts.ConsoleHost/bin/Release/net10.0/`. The folder publish profile also targets .NET 10.

## Configuration

Configuration loads in the following order; later sources override earlier ones:

1. Required `appsettings.json` from the host content root, which defaults to the executable directory.
2. Optional `appsettings.<environment>.json` from the same directory.
3. Optional User Secrets using the existing ConsoleHost identifier.
4. Environment variables with the `ORABOT_` prefix.
5. Command-line options.

User Secrets remain available in every environment for compatibility with existing installations. They are useful for development and are unencrypted. Prefer an environment or protected deployment-specific secret source for production credentials. Command-line options are suitable for nonsecret overrides.

Use double underscores for nested environment-variable keys, for example `ORABOT_MapAnnouncer__ScanInterval`. `DOTNET_ENVIRONMENT` selects the host environment; its default is `Production`.

The host validates configuration before constructing the Discord service graph. Errors identify invalid keys without printing their values. Existing role settings, documentation/icon URLs and storage paths retain their configuration names. Empty role allowlists and reaction message ID `0` remain valid.

Connection and polling options include:

| Key | Meaning |
| --- | --- |
| `BotToken` | Required Discord bot token |
| `MessageCacheSize` | Bounded SDK message cache; defaults to 100, accepts 0 through 10000 |
| `DocsSearchIndex:ScanInterval` | Positive refresh interval in milliseconds; defaults to 300000 |
| `MapAnnouncer:ServerName` | Required destination server name |
| `MapAnnouncer:ChannelName` | Required destination channel name |
| `MapAnnouncer:ScanInterval` | Positive polling interval in milliseconds; defaults to 60000 |

Log/replay storage settings retain their existing path semantics. Use absolute paths when launching from different working directories, and provision the storage folders before processing attachments. Validation checks syntax without creating directories.

## Console lifetime and background work

The Generic Host owns the Discord connection and both polling workers. Use **Ctrl+C** for graceful shutdown. Standard input no longer controls the process lifetime.

Repeated gateway readiness events update connectivity state and never start another worker. Map polling waits for Discord readiness; documentation refresh can warm its cache independently. A complete documentation generation is published atomically, and failed refreshes retain the last good generation.

Polling requests and delays observe cancellation. The host stops workers before closing the Discord connection. It requests cooperative shutdown with a 30-second budget; Discord.Net's stop/logout operations have no cancellation parameter and are awaited. Live transport teardown still needs test-guild verification.

Recoverable upstream failures are logged and retried on the configured interval. Unexpected worker failure stops the host and produces a nonzero process exit. SDK and worker diagnostics record safe metadata rather than raw transport payloads or message content.

Map startup still establishes its initial cursor without announcing the existing backlog. The existing ten-map polling policy remains in place.

The socket client subscribes explicitly to guild/message/reaction events and Message Content. Enable the required Message Content intent for the application: passive issue/map recognition and attachment processing still depend on it.

## Subsequent modernization

Prefix commands and custom slash/autocomplete dispatch remain in place. Slash-command publication is still disabled during normal startup. Existing discarded event-handler tasks, quote async operations and attachment processing are subsequent interaction/feature work.

The next stage introduces `InteractionService`, documentation command modules and explicit command publication. Remaining prefix commands then migrate to slash commands, followed by the operational finish and test-guild rollout verification.

### Temporary configuration bridge

`LegacyFeatureOptions` and its validator pre-validate settings that older feature constructors still read through `IConfiguration`. As each feature adopts its own typed options, move its validation and test cases into that feature and remove its entries from this bridge. Remove the bridge and its test class after the final legacy consumer is migrated. The explicit validation before hosted-service construction can then be reviewed for removal; ordinary startup validation remains.
