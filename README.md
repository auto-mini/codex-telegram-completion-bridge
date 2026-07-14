# Codex Telegram Completion Bridge

Windows sidecar that sends one completion-only Telegram message when a user-visible root Codex task finishes, while preserving an existing verified Computer Use notifier.

The message contains a status line, the PC name or configured alias, and at most the first 12 Unicode grapheme clusters of the visible task title. It does not send the prompt, response, transcript, or task files.

> **Release status:** the first public release is an unsigned preview needed for public review and the SignPath Foundation application. Windows may block it. Do not disable Smart App Control, Microsoft Defender, or another security control to run an unsigned artifact. The proven local canary remains the operational build until a signed release passes the full qualification gate.

This is an independent project and is not affiliated with or endorsed by OpenAI, Telegram, or Microsoft.

## How it works

The bridge installs as a per-user Codex `notify` hook. It validates a completion event, keeps the existing upstream notifier intact, writes a durable local queue, and lets a per-user scheduled worker deliver the Telegram message. Duplicate events are suppressed. Delivery retries are bounded and honor Telegram rate limits. For current Codex desktop tasks, the visible title in app metadata takes precedence over the legacy state-database title so a rename survives an app or PC restart; the database title remains a fallback only when no app title exists.

Installation begins in `shadow` mode with no Telegram network activity. The user verifies sampled PC names and visible titles before entering a bot token and enabling live delivery. A title shorter than 12 grapheme clusters is verified by entering the complete title; longer titles require a prefix of at least 12 grapheme clusters.

Security boundaries include:

- bot tokens accepted only through an interactive no-echo prompt;
- credentials encrypted with Windows DPAPI for the current user;
- a private one-to-one Telegram chat only, with webhooks rejected;
- transactional Codex configuration changes and rollback records;
- manifest verification, restrictive local ACLs, bounded logs, and fail-closed upstream handling; and
- no bundled native SQLite library: the Windows-serviced `winsqlite3.dll` is used.

See [PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), the [validation status](docs/validation.md), and the [code signing policy](docs/code-signing-policy.md).

For the project owner's two-PC private pilot only, [the personal rollout design](docs/personal-two-pc-rollout.md) describes a fail-closed exact-hash supplemental Smart App Control policy. It is not a general unsigned-install recommendation and never disables Windows protections.

## Requirements

- Windows 10 or Windows 11, x64
- Codex desktop configuration for the current Windows user
- a Telegram account and a bot created through BotFather for live delivery
- .NET 8 SDK only when building from source; release packages are self-contained

## Development

```powershell
dotnet restore .\CodexTelegramBridge.sln --locked-mode
dotnet build .\CodexTelegramBridge.sln -c Release --no-restore
dotnet test .\CodexTelegramBridge.sln -c Release --no-build
& .\scripts\assert-no-vulnerable-packages.ps1
```

Do not install or enable live delivery until the automated tests and the current-PC shadow gate pass.

## Build a release package

```powershell
& .\scripts\package.ps1 -Version "0.1.0-preview.1"
```

The versioned directory under `artifacts\release` contains two self-contained Windows x64 applications, deployment scripts, policy and license documents, a dependency inventory, and `manifest.sha256`. Keep the sibling `*.manifest.outer.sha256` record separately from the package.

The package embeds a uniform Windows file version derived from the SemVer core. It fails if a native `e_sqlite3.dll` unexpectedly enters the output. Exact dependency versions are locked.

## Install and verify

Installation is split into a read-only plan and an explicit apply step. Applying requires Codex/ChatGPT desktop and its app server to be closed.

```powershell
& .\scripts\install.ps1 -PlanPath "$env:TEMP\CodexTelegramBridge-install-plan.json"
# Review the plan, close Codex/ChatGPT, then create a fresh plan and apply within 30 minutes:
& .\scripts\install.ps1 -PlanPath "$env:TEMP\CodexTelegramBridge-install-plan.json" -Apply
```

The first install performs no Telegram network activity. Operational commands are available from:

```powershell
$ctl = "$env:LOCALAPPDATA\CodexTelegramBridge\bin\CodexTelegramCtl.exe"
& $ctl doctor --json
& $ctl shadow list
& $ctl shadow verify 1
& $ctl quarantine list
& $ctl quarantine acknowledge 1
& $ctl telegram bootstrap
& $ctl enable-live
& $ctl pause
& $ctl resume
```

`shadow verify` asks, with no echo, for the exact PC name and at least the first 12 visible characters of the task title. A copied trailing ellipsis is ignored, so a sidebar-truncated title remains verifiable without displaying the full locally stored title.

`quarantine list` displays only sequence, timestamp, capture mode, fixed error code, and a shortened opaque event ID. `quarantine acknowledge <sequence>` requires interactive confirmation, converts exactly that reviewed row to `suppressed`, and clears `EVENT_QUARANTINED` only after no quarantined rows remain. It never sends or replays the event.

After desktop startup, Computer Use may place its verified `turn-ended --previous-notify` wrapper around the bridge. This is supported; the bridge suppresses a second upstream launch so Computer Use is signaled only once.

Never place a bot token in a command line, environment variable, plan, issue, log, or deployment record.

## Rollback and removal

```powershell
& .\scripts\uninstall.ps1
```

Removal restores the captured upstream notifier when the bridge is direct, or safely unwraps the bridge while leaving a verified outer Computer Use handler. Any unsupported nested bridge reference fails closed. Mutable state and encrypted backups are retained unless `-PurgeState` is explicitly confirmed.

## Code signing policy

Planned production releases use: **Free code signing provided by SignPath.io, certificate by SignPath Foundation.** The service is not active yet. Every release page explicitly identifies whether its assets are unsigned or signed.

Project roles:

- Committer and reviewer: [@auto-mini](https://github.com/auto-mini)
- Signing approver: [@auto-mini](https://github.com/auto-mini)

Every signing request requires manual approval. Only the two project executables and the install/uninstall scripts may be signed; no upstream binary is signed with the project certificate. Full rules and activation gates are in the [code signing policy](docs/code-signing-policy.md).

## License

The project is available under the [MIT License](LICENSE). Third-party terms are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
