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
    $common -match 'Test-SmartAppControlExamplePolicy' -or
    $install -match 'Invoke-CiToolJson' -or
    ([regex]::Matches($install, '--update-policy')).Count -ne 0 -or
    ([regex]::Matches($install, '--remove-policy')).Count -ne 0 -or
    ([regex]::Matches($remove, '--remove-policy')).Count -ne 1 -or
    $remove -notmatch 'BASE_POLICY_REMOVAL_REFUSED' -or
    $remove -notmatch 'PROJECT_POLICY_REMOVAL_IDENTITY_MISMATCH' -or
    $remove -notmatch 'Get-PersonalProjectPolicyState' -or
    $common -notmatch '\$isPresent = \$matches\.Count -ne 0' -or
    $candidateExecution -notmatch '\[Parameter\(Mandatory = \$true\)\]\[AllowEmptyString\(\)\]\[string\]\$Arguments') {
    throw "Personal-rollout safety guards are incomplete."
}

. (Join-Path $runtimeRoot "PersonalRollout.Common.ps1")

$policyMetadata = [pscustomobject]@{
    PolicyId = "FC005318-2251-4387-8964-0BCF777763EA"
    BasePolicyId = "0283AC0F-FFF1-49AE-ADA1-8A933130CAD6"
    FriendlyName = "CodexTelegramBridge-Personal-Allow"
    Version = "1.0.0.4"
}

function New-MockProjectPolicy {
    param(
        [bool]$Authorized,
        [bool]$Enforced,
        [string]$Version = "1.0.0.4",
        [switch]$OmitAuthorized
    )

    $properties = [ordered]@{
        PolicyID = $policyMetadata.PolicyId
        BasePolicyID = $policyMetadata.BasePolicyId
        FriendlyName = $policyMetadata.FriendlyName
        VersionString = $Version
        IsSystemPolicy = $false
        IsSignedPolicy = $false
        IsOnDisk = $true
        IsEnforced = $Enforced
        PolicyOptions = @("Enabled:Unsigned System Integrity Policy")
    }
    if (-not $OmitAuthorized) {
        $properties.IsAuthorized = $Authorized
    }

    return [pscustomobject]$properties
}

function Assert-Decision {
    param(
        [Parameter(Mandatory = $true)][object]$Decision,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string[]]$Reasons = @()
    )

    if ($Decision.Mode -ne $Mode) {
        throw "Unexpected rollout mode: $($Decision.Mode); expected=$Mode"
    }
    foreach ($reason in $Reasons) {
        if ($Decision.Reasons -notcontains $reason) {
            throw "Missing rollout reason: $reason"
        }
    }
}

$absentState = Get-PersonalProjectPolicyState -Policies @() -PolicyMetadata $policyMetadata
$absentDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 1 -SacBaseCount 1 -ExtraBasePolicyCount 0 -ProjectPolicyState $absentState
Assert-Decision -Decision $absentDecision -Mode "BLOCKED" -Reasons @("SAC_UNSIGNED_SUPPLEMENTAL_NOT_AUTHORIZED")

$unauthorizedState = Get-PersonalProjectPolicyState -Policies @((New-MockProjectPolicy -Authorized $false -Enforced $false)) -PolicyMetadata $policyMetadata
$unauthorizedDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 1 -SacBaseCount 1 -ExtraBasePolicyCount 0 -ProjectPolicyState $unauthorizedState
if (-not $unauthorizedState.IdentityValid -or $unauthorizedState.Authorized -or $unauthorizedState.Eligible) {
    throw "Unauthorized project-policy state was not classified fail-closed."
}
Assert-Decision -Decision $unauthorizedDecision -Mode "BLOCKED" -Reasons @("PROJECT_POLICY_NOT_AUTHORIZED", "PROJECT_POLICY_NOT_ENFORCED")

$eligibleState = Get-PersonalProjectPolicyState -Policies @((New-MockProjectPolicy -Authorized $true -Enforced $true)) -PolicyMetadata $policyMetadata
$eligibleDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 1 -SacBaseCount 1 -ExtraBasePolicyCount 0 -ProjectPolicyState $eligibleState
if (-not $eligibleState.Eligible) {
    throw "Exact authorized project policy was not classified eligible."
}
Assert-Decision -Decision $eligibleDecision -Mode "SAC_ENFORCED_READY"

$mismatchState = Get-PersonalProjectPolicyState -Policies @((New-MockProjectPolicy -Authorized $true -Enforced $true -Version "1.0.0.3")) -PolicyMetadata $policyMetadata
$mismatchDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 1 -SacBaseCount 1 -ExtraBasePolicyCount 0 -ProjectPolicyState $mismatchState
Assert-Decision -Decision $mismatchDecision -Mode "BLOCKED" -Reasons @("PROJECT_POLICY_IDENTITY_MISMATCH")

$missingPropertyState = Get-PersonalProjectPolicyState -Policies @((New-MockProjectPolicy -Authorized $false -Enforced $true -OmitAuthorized)) -PolicyMetadata $policyMetadata
$missingPropertyDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 1 -SacBaseCount 1 -ExtraBasePolicyCount 0 -ProjectPolicyState $missingPropertyState
Assert-Decision -Decision $missingPropertyDecision -Mode "BLOCKED" -Reasons @("PROJECT_POLICY_NOT_AUTHORIZED")

$sacOffDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 0 -SacBaseCount 0 -ExtraBasePolicyCount 0 -ProjectPolicyState $absentState
Assert-Decision -Decision $sacOffDecision -Mode "SAC_OFF_DIRECT_TEST"

$unexpectedSacOffDecision = Get-PersonalRolloutDecision -Build 26100 -SmartAppControlState 0 -SacBaseCount 0 -ExtraBasePolicyCount 0 -ProjectPolicyState $eligibleState
Assert-Decision -Decision $unexpectedSacOffDecision -Mode "BLOCKED" -Reasons @("PROJECT_POLICY_UNEXPECTED_WHILE_SAC_OFF")

Write-Output "personal_rollout_safety=pass"
