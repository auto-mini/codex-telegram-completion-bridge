[CmdletBinding()]
param(
    [string]$PlanPath = (Join-Path $env:TEMP "CodexTelegramBridge-install-plan.json"),
    [string]$PcAlias,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ctl = Join-Path $releaseRoot "bin\CodexTelegramCtl.exe"
if (-not (Test-Path -LiteralPath $ctl -PathType Leaf)) {
    throw "CodexTelegramCtl.exe is missing from this release."
}

$arguments = @("install", "--plan", $PlanPath, "--package-root", $releaseRoot)
if ($PcAlias) {
    $arguments += @("--pc-alias", $PcAlias)
}
& $ctl @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $Apply) {
    Write-Output "Read the plan, close Codex/ChatGPT desktop, then run again with -Apply."
    exit 0
}

& $ctl install --apply $PlanPath
exit $LASTEXITCODE
