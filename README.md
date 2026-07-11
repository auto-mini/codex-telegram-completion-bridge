# Codex Telegram Completion Bridge

Windows sidecar that preserves the existing Codex Computer Use notifier and sends completion-only Telegram messages for user-visible root tasks.

The frozen architecture and rollout gates are in [`docs/20260711_codex_telegram_completion_notification_blueprint.md`](docs/20260711_codex_telegram_completion_notification_blueprint.md).

## Development

```powershell
$dotnet = "$env:LOCALAPPDATA\dotnet\dotnet.exe"
& $dotnet restore .\CodexTelegramBridge.sln --locked-mode
& $dotnet build .\CodexTelegramBridge.sln -c Release --no-restore
& $dotnet test .\CodexTelegramBridge.sln -c Release --no-build
```

Do not install or enable live delivery until the automated tests and the current-PC shadow gate pass.

## Build a release

```powershell
& .\scripts\package.ps1 -Version "1.0.0-canary.1"
```

The versioned directory under `artifacts/release` is self-contained for Windows x64. Keep the sibling `*.manifest.outer.sha256` record separately from the package.

## Current-PC rollout

Installation is deliberately split into a read-only plan and an apply step. Applying requires Codex/ChatGPT desktop and its app server to be closed.

```powershell
& .\scripts\install.ps1 -PlanPath "$env:TEMP\CodexTelegramBridge-install-plan.json"
# Review the plan, close Codex/ChatGPT, then create a fresh plan and apply within 30 minutes:
& .\scripts\install.ps1 -PlanPath "$env:TEMP\CodexTelegramBridge-install-plan.json" -Apply
```

The first install remains in `shadow` mode and performs no Telegram network activity. Operational commands are available from:

```powershell
$ctl = "$env:LOCALAPPDATA\CodexTelegramBridge\bin\CodexTelegramCtl.exe"
& $ctl doctor --json
& $ctl shadow list
& $ctl shadow verify 1
& $ctl telegram bootstrap
& $ctl enable-live
& $ctl pause
& $ctl resume
```

After desktop startup, Computer Use may place its verified `turn-ended --previous-notify` wrapper around the bridge. This is a supported active shape; `doctor` reports it as `BRIDGE_ACTIVE_WRAPPED`, and the bridge suppresses a second upstream launch so Computer Use is signaled only once.

Bot tokens are accepted only through an interactive no-echo prompt. Never place a token in a command line, environment variable, plan, issue, log, or deployment record.

## Rollback and removal

```powershell
& .\scripts\uninstall.ps1
```

Removal restores the captured upstream when the bridge is direct, or safely unwraps the bridge while leaving a verified outer Computer Use handler. Any unsupported nested bridge reference fails closed. Mutable state and encrypted backups are retained unless `-PurgeState` is explicitly confirmed.
