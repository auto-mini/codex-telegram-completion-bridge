# Contributing

Issues and pull requests are welcome. Never include Telegram tokens, chat identifiers, private Codex content, local state, real computer names, or user-profile paths in a report, fixture, screenshot, commit, or build log.

## Development checks

Use the .NET SDK selected by `global.json`:

```powershell
dotnet restore .\CodexTelegramBridge.sln --locked-mode
dotnet build .\CodexTelegramBridge.sln -c Release --no-restore
dotnet test .\CodexTelegramBridge.sln -c Release --no-build
& .\scripts\assert-no-vulnerable-packages.ps1
```

Changes to installation, uninstallation, Codex configuration editing, scheduled tasks, release workflows, or `.signpath` policy files require focused tests and maintainer review. Pull requests from people without direct commit access must be approved before merge.

## Release authority

The roles and manual approval requirements for signed releases are defined in the [code signing policy](docs/code-signing-policy.md). A contribution does not grant signing or release authority.
