[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $root "scripts\personal-rollout"
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -Filter "*.ps1" -File -Recurse)
$runtime = ($runtimeFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
$forbidden = @(
    '\bUnblock-File\b',
    '\b(?:Set|Add)-MpPreference\b',
    '\bSet-NetFirewall',
    '\b(?:Set|New|Remove)-ItemProperty\b',
    '\breg(?:\.exe)?\s+add\b',
    '\bmanage-bde\b',
    '\bbcdedit\b',
    '\btakeown\b',
    '\bicacls\b',
    'DisableRealtimeMonitoring',
    'CiPolicies[\\/]Active',
    'VerifiedAndReputablePolicyState\s*='
)
foreach ($pattern in $forbidden) {
    if ($runtime -match $pattern) {
        throw "Forbidden personal-rollout mutation pattern: $pattern"
    }
}

$common = Get-Content -LiteralPath (Join-Path $runtimeRoot "PersonalRollout.Common.ps1") -Raw
$install = Get-Content -LiteralPath (Join-Path $runtimeRoot "Install-PersonalSupplementalPolicy.ps1") -Raw
$remove = Get-Content -LiteralPath (Join-Path $runtimeRoot "Remove-PersonalSupplementalPolicy.ps1") -Raw
$candidateExecution = Get-Content -LiteralPath (Join-Path $runtimeRoot "Test-PersonalCandidateExecution.ps1") -Raw
if ($common -notmatch '0283AC0F-FFF1-49AE-ADA1-8A933130CAD6' -or
    ([regex]::Matches($install, '--update-policy')).Count -ne 1 -or
    ([regex]::Matches($install, '--remove-policy')).Count -ne 1 -or
    ([regex]::Matches($remove, '--remove-policy')).Count -ne 1 -or
    $remove -notmatch 'BASE_POLICY_REMOVAL_REFUSED' -or
    $remove -notmatch 'PROJECT_POLICY_REMOVAL_IDENTITY_MISMATCH' -or
    $candidateExecution -notmatch '\[Parameter\(Mandatory = \$true\)\]\[AllowEmptyString\(\)\]\[string\]\$Arguments') {
    throw "Personal-rollout safety guards are incomplete."
}

Write-Output "personal_rollout_safety=pass"
