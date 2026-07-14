[CmdletBinding()]
param(
    [string]$BundleRoot,
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = Join-Path $PSScriptRoot ".."
}
. (Join-Path $PSScriptRoot "PersonalRollout.Common.ps1")

if (-not $Apply) {
    [Console]::Error.WriteLine("APPLY_SWITCH_REQUIRED")
    exit 2
}

try {
    [void](Assert-PersonalBundleIntegrity -BundleRoot $BundleRoot)
    $rollout = Read-PersonalRolloutMetadata -BundleRoot $BundleRoot
    $policy = Get-PersonalPolicyXmlMetadata -BundleRoot $BundleRoot -RolloutMetadata $rollout
    Assert-IsAdministrator

    if ($policy.PolicyId -eq $policy.BasePolicyId -or $policy.PolicyId -eq $script:SacEnforcementBasePolicyId) {
        throw "BASE_POLICY_REMOVAL_REFUSED"
    }

    $policies = Get-CiPolicyInventory
    $projectPolicyState = Get-PersonalProjectPolicyState -Policies $policies -PolicyMetadata $policy
    if ($projectPolicyState.Count -eq 0) {
        Write-Output "policy=already_absent"
        Write-Output "policy_id=$($policy.PolicyId)"
        exit 0
    }

    if (-not $projectPolicyState.Present -or -not $projectPolicyState.IdentityValid) {
        throw "PROJECT_POLICY_REMOVAL_IDENTITY_MISMATCH"
    }

    [void](Invoke-CiToolJson -Arguments @("--remove-policy", "{$($policy.PolicyId)}", "-json"))
    $removal = Wait-ForPolicyState -PolicyId $policy.PolicyId -Present $false
    if (-not $removal.Matched) {
        Write-Output "policy=removal_pending"
        Write-Output "policy_id=$($policy.PolicyId)"
        Write-Output "reboot_required=yes"
        exit 1
    }

    Write-Output "policy=removed"
    Write-Output "policy_id=$($policy.PolicyId)"
    Write-Output "reboot_required=no"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 3
}
