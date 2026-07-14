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

$installedByThisRun = $false
try {
    [void](Assert-PersonalBundleIntegrity -BundleRoot $BundleRoot)
    $rollout = Read-PersonalRolloutMetadata -BundleRoot $BundleRoot
    $policy = Get-PersonalPolicyXmlMetadata -BundleRoot $BundleRoot -RolloutMetadata $rollout
    Assert-IsAdministrator

    $policies = Get-CiPolicyInventory
    $readiness = Get-PersonalRolloutReadiness -Policies $policies -PolicyMetadata $policy
    if ($readiness.Mode -ne "SAC_ENFORCED_READY") {
        throw "POLICY_INSTALL_NOT_ALLOWED_$($readiness.Mode)"
    }

    $existing = @(Get-PolicyById -Policies $policies -PolicyId $policy.PolicyId)
    if ($existing.Count -eq 1) {
        if ((ConvertTo-NormalizedGuid $existing[0].BasePolicyID) -ne $policy.BasePolicyId -or
            -not [string]::Equals([string]$existing[0].FriendlyName, $policy.FriendlyName, [StringComparison]::Ordinal) -or
            -not (ConvertTo-StrictBoolean $existing[0].IsEnforced)) {
            throw "EXISTING_PROJECT_POLICY_IDENTITY_MISMATCH"
        }

        Write-Output "policy=already_active"
        Write-Output "policy_id=$($policy.PolicyId)"
        exit 0
    }

    if ($existing.Count -ne 0) {
        throw "EXISTING_PROJECT_POLICY_AMBIGUOUS"
    }

    [void](Invoke-CiToolJson -Arguments @("--update-policy", $policy.CipPath, "-json"))
    $installedByThisRun = $true
    $activation = Wait-ForPolicyState -PolicyId $policy.PolicyId -Present $true
    if (-not $activation.Matched -or
        (ConvertTo-NormalizedGuid $activation.Policy.BasePolicyID) -ne $policy.BasePolicyId -or
        -not [string]::Equals([string]$activation.Policy.FriendlyName, $policy.FriendlyName, [StringComparison]::Ordinal)) {
        throw "PROJECT_POLICY_ACTIVATION_FAILED"
    }

    Write-Output "policy=active"
    Write-Output "policy_id=$($policy.PolicyId)"
    Write-Output "base_policy_id=$($policy.BasePolicyId)"
    Write-Output "reboot_required=no"
    exit 0
}
catch {
    $failure = $_.Exception.Message
    if ($installedByThisRun) {
        try {
            [void](Invoke-CiToolJson -Arguments @("--remove-policy", "{$($policy.PolicyId)}", "-json"))
            $rollback = Wait-ForPolicyState -PolicyId $policy.PolicyId -Present $false
            if ($rollback.Matched) {
                [Console]::Error.WriteLine("$failure; rollback=complete")
            }
            else {
                [Console]::Error.WriteLine("$failure; rollback=pending_reboot")
            }
        }
        catch {
            [Console]::Error.WriteLine("$failure; rollback=manual_recovery_required; rollback_error=$($_.Exception.Message)")
        }
    }
    else {
        [Console]::Error.WriteLine($failure)
    }

    exit 3
}
