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
    $matches = @(Get-PolicyById -Policies $policies -PolicyId $policy.PolicyId)
    if ($matches.Count -eq 0) {
        Write-Output "policy=already_absent"
        Write-Output "policy_id=$($policy.PolicyId)"
        exit 0
    }

    if ($matches.Count -ne 1 -or
        (ConvertTo-NormalizedGuid $matches[0].BasePolicyID) -ne $policy.BasePolicyId -or
        -not [string]::Equals([string]$matches[0].FriendlyName, $policy.FriendlyName, [StringComparison]::Ordinal) -or
        ($null -ne $matches[0].PSObject.Properties["IsSystemPolicy"] -and (ConvertTo-StrictBoolean $matches[0].IsSystemPolicy))) {
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
