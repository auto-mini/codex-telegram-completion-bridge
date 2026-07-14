[CmdletBinding()]
param(
    [string]$BundleRoot,
    [string]$PcAlias
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = Join-Path $PSScriptRoot ".."
}
. (Join-Path $PSScriptRoot "PersonalRollout.Common.ps1")

try {
    [void](Assert-PersonalBundleIntegrity -BundleRoot $BundleRoot)
    $rollout = Read-PersonalRolloutMetadata -BundleRoot $BundleRoot
    $packageRoot = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$rollout.final.packageRelativePath)
    $ctl = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$rollout.final.ctlRelativePath)
    $waiter = Join-Path $PSScriptRoot "Complete-PersonalBridgeInstall.ps1"
    if (-not (Test-Path -LiteralPath $waiter -PathType Leaf)) {
        throw "INSTALL_WAITER_MISSING"
    }

    $id = [guid]::NewGuid().ToString("N")
    $plan = Join-Path $env:TEMP "CodexTelegramBridge-install-plan-$id.json"
    $result = Join-Path $env:TEMP "CodexTelegramBridge-install-result-$id.json"
    $arguments = @("install", "--plan", $plan, "--package-root", $packageRoot)
    if (-not [string]::IsNullOrWhiteSpace($PcAlias)) {
        $arguments += @("--pc-alias", $PcAlias)
    }

    & $ctl @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "INSTALL_PLAN_FAILED_$LASTEXITCODE"
    }

    function Quote-PowerShellLiteral([string]$Value) {
        return "'" + $Value.Replace("'", "''") + "'"
    }
    $command = "& $(Quote-PowerShellLiteral $waiter) -PlanPath $(Quote-PowerShellLiteral $plan) -CtlPath $(Quote-PowerShellLiteral $ctl) -ResultPath $(Quote-PowerShellLiteral $result)"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $powerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $process = Start-Process -FilePath $powerShell -ArgumentList @("-NoLogo", "-NoProfile", "-EncodedCommand", $encoded) -WindowStyle Normal -PassThru
    if ($null -eq $process) {
        throw "INSTALL_WAITER_START_FAILED"
    }

    Write-Output "install_waiter=started"
    Write-Output "waiter_pid=$($process.Id)"
    Write-Output "result_path=$result"
    Write-Output "next_action=close_codex_desktop_without_killing_the_waiter"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 3
}
