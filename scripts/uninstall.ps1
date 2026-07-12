[CmdletBinding()]
param(
    [switch]$KeepConfigConflict,
    [switch]$PurgeState
)

$ErrorActionPreference = "Stop"
$ctl = Join-Path $env:LOCALAPPDATA "CodexTelegramBridge\bin\CodexTelegramCtl.exe"
if (-not (Test-Path -LiteralPath $ctl -PathType Leaf)) {
    throw "The installed CodexTelegramCtl.exe was not found."
}

$arguments = @("uninstall")
if ($KeepConfigConflict) { $arguments += "--keep-config-conflict" }
if ($PurgeState) { $arguments += "--purge-state" }
& $ctl @arguments
exit $LASTEXITCODE
