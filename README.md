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
