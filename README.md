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
  Orabot.Tests/             Offline configuration, lifecycle, interaction and deployment tests
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

Tests use synthetic configuration, fake APIs and fake time. SDK metadata, interaction execution and deployment behavior are exercised against offline fixtures. Tests do not log into Discord or read the operator's User Secrets store.

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

Documentation owns typed validation for the existing `Traits`, `Weapons`, `Lua` and `SpriteSequences` sections. Each keeps `ReleasePageUrl`, `PlaytestPageUrl` and `DevelopmentPageUrl`; the shared documentation icon remains `OpenRaFaviconUrl`. HTTP/HTTPS URLs are required and embedded credentials are rejected. These settings have left the temporary legacy validation bridge.

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

The Generic Host owns the Discord connection, interaction work queue and both polling workers. Use **Ctrl+C** for graceful shutdown. Standard input no longer controls the process lifetime.

Repeated gateway readiness events update connectivity state and never start another worker. Map polling waits for Discord readiness; documentation refresh can warm its cache independently. A complete documentation generation is published atomically, and failed refreshes retain the last good generation.

Polling requests and delays observe cancellation. The host stops polling workers and the interaction queue before closing the Discord connection. It requests cooperative shutdown with a 30-second budget; Discord.Net's stop/logout operations have no cancellation parameter and are awaited. Live transport teardown still needs test-guild verification.

Recoverable upstream failures are logged and retried on the configured interval. Unexpected worker failure stops the host and produces a nonzero process exit. SDK and worker diagnostics record safe metadata rather than raw transport payloads or message content.

Map startup still establishes its initial cursor without announcing the existing backlog. The existing ten-map polling policy remains in place.

The socket client subscribes explicitly to guild/message/reaction events and Message Content. Enable the required Message Content intent for the application: passive issue/map recognition and attachment processing still depend on it.

## Slash commands and documentation

The ten existing slash commands are now attributed Discord.Net `InteractionService` modules: `supportdir`, `academy`, `book`, `sdk`, `utility`, `orabot`, `lua`, `traits`, `weapons` and `sprite-sequences`. Documentation keeps optional `name` autocomplete and optional `version` choices `release`, `playtest` and `development`, defaulting to `release`.

Ordinary slash commands receive one **public deferred response**, then edit that response when execution completes. A bounded queue has four execution workers and room for 64 pending commands; a full queue returns a busy response. SDK execution uses synchronous run mode within those workers so the command task and its dependency scope remain owned and awaited.

Autocomplete responds directly from the in-memory documentation catalog and performs no HTTP requests. Cold caches return empty suggestions; results are limited to 25 choices with bounded labels and values. Each autocomplete callback owns one response bounded by the acknowledgment deadline, and an uncertain transport failure never triggers a second attempt. Submitted options are validated again by the server.

Named documentation lookups use a ten-second request/body deadline, a four-MiB decoded HTML budget and a per-document nesting limit. An unknown heading retains the family-page fallback. HTTP lookup, catalog refresh and embed rendering have separate responsibilities; rendering performs no network access. Upstream failures complete the deferred command with a clear error.

## Publishing application commands

Normal bot startup loads command metadata for dispatch and **never publishes or deletes commands**. Deployment is explicit:

- `commands export [--global | --guild <id>]` exports the local schema; the default is global. After building, export is entirely offline and needs no configuration or token.
- `commands plan (--global | --guild <id>) [--prune]` reads the selected Discord scope and prints the proposed changes and a plan fingerprint.
- `commands apply (--global | --guild <id>) [--prune] [--expected-plan <fingerprint>]` prints the current plan and applies individual changes. Plan and apply require an explicit target.

Plan/apply use `BotToken` from the normal configuration sources, including `ORABOT_BotToken`. The deployment CLI has no token argument. These modes use REST without starting the Gateway, polling workers or normal bot service graph.

From the repository root, after the Release build above, PowerShell examples are:

```powershell
# Offline exports for each deployment scope.
dotnet run --project src/Orabot.Hosts.ConsoleHost -c Release --no-build -- commands export --global
$guildId = "123456789012345678" # Replace with the test server's ID.
dotnet run --project src/Orabot.Hosts.ConsoleHost -c Release --no-build -- commands export --guild $guildId

# Read and review a server plan, then apply only that reviewed fingerprint.
$plan = (dotnet run --project src/Orabot.Hosts.ConsoleHost -c Release --no-build -- commands plan --guild $guildId | Out-String) | ConvertFrom-Json
$plan | ConvertTo-Json -Depth 30
dotnet run --project src/Orabot.Hosts.ConsoleHost -c Release --no-build -- commands apply --guild $guildId --expected-plan $plan.fingerprint

# Read a global deployment plan when ready to promote the schema.
dotnet run --project src/Orabot.Hosts.ConsoleHost -c Release --no-build -- commands plan --global
```

Guild deployment targets one server. Global definitions allow guild and bot-DM contexts with guild installation; user installation is not enabled. Guild endpoints omit global-only context/install fields. Test in a guild before promoting global definitions.

Unchanged managed commands are left alone. Extra remote commands are preserved by default. **`--prune` permits deletion of every extra command in the selected scope**, including commands outside this local catalog; include it in both the reviewed plan and apply when deletions are intended.

The optional `--expected-plan` rejects changes to the reviewed scope, definitions, prune mode or remote state. Apply also checks remote state again before writing. Writes are individual and non-atomic: cancellation or a failure can leave earlier changes applied, so generate and review a new plan before retrying. Discord.Net 3.20.2's global-delete path does not forward all request cancellation/retry options, so an in-flight delete may finish after cancellation.

Explicit zero default-member permissions are rejected: Discord.Net 3.20.2's remote model conflates API `null` and `"0"`, preventing a reliable comparison for that case.

## Subsequent modernization

Stage 4 migrates the remaining prefix commands (`help`, `hi`, `say`, `quote` and `role`) and completes ownership/bounds for remaining message, quote and attachment work. Legacy `WebClient` and `Uri.EscapeUriString` warnings remain in that code. Stage 5 covers the operational finish and live test-guild rollout verification.

### Temporary configuration bridge

`LegacyFeatureOptions` and its validator now cover only remaining legacy GitHub-icon, role and log/replay-storage settings read through `IConfiguration`. Documentation owns its typed options and validation. As each remaining feature migrates, move its validation and test cases into that feature and remove its entries from the bridge. Remove the bridge and its tests after its final consumer is migrated; ordinary startup validation remains.
