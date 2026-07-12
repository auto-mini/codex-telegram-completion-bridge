# Codex Telegram Completion Bridge

Windows sidecar that preserves the existing Codex Computer Use notifier and sends completion-only Telegram messages for user-visible root tasks.

Telegram messages show at most the first 32 Unicode grapheme clusters of a task title followed by `…`; short titles are unchanged. The longer normalized title remains only in the local encrypted envelope for identity verification and is never written to operational logs.

> **Smart App Control:** development canaries are currently unsigned. Windows 11 Smart App Control can block every new executable hash, including scheduled-task launches. Do not disable Smart App Control or add security exclusions. The 32-grapheme build and any multi-PC release require Authenticode signatures chaining to a CA in the Microsoft Trusted Root Program before deployment. The current canary PC is temporarily running the previously trusted canary.9 build, which does not include the 32-grapheme display refinement.

The selected production path is a one-year Certum Standard Code Signing in the Cloud certificate for an individual. No certificate has been purchased yet. The decision, privacy/cost boundary, pinned trust chain, and qualification procedure are in [`docs/code-signing-runbook.md`](docs/code-signing-runbook.md).

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

Unsigned builds are development-only. After the selected certificate is activated, build an eligible signed candidate with:

```powershell
& .\scripts\bootstrap-signing-tools.ps1
& .\scripts\package.ps1 `
    -Version "1.0.0-rc.1" `
    -CodeSigningThumbprint "<40-hex-certificate-thumbprint>" `
    -RequireCodeSigning
```

The package command keeps the native SQLite DLL beside the single-file applications instead of extracting it to a temporary directory, then signs both executables, that DLL, and both deployment scripts before generating the manifest. A signed package contains `signing-record.json`; install planning, apply, staging, and doctor reject it if Windows Authenticode trust, the timestamp, the selected signer, the pinned Certum signing CA, or the pinned public root chain does not validate.

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

`shadow verify` asks, with no echo, for the exact PC name and at least the first 12 visible characters of the task title. A copied trailing `…` or `...` is ignored, so a sidebar-truncated title remains verifiable without exposing the full stored title.

After desktop startup, Computer Use may place its verified `turn-ended --previous-notify` wrapper around the bridge. This is a supported active shape; `doctor` reports it as `BRIDGE_ACTIVE_WRAPPED`, and the bridge suppresses a second upstream launch so Computer Use is signaled only once.

Bot tokens are accepted only through an interactive no-echo prompt. Never place a token in a command line, environment variable, plan, issue, log, or deployment record.

## Rollback and removal

```powershell
& .\scripts\uninstall.ps1
```

Removal restores the captured upstream when the bridge is direct, or safely unwraps the bridge while leaving a verified outer Computer Use handler. Any unsupported nested bridge reference fails closed. Mutable state and encrypted backups are retained unless `-PurgeState` is explicitly confirmed.
