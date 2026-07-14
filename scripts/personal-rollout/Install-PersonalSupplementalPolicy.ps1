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

    $policies = Get-CiPolicyInventory
    $readiness = Get-PersonalRolloutReadiness -Policies $policies -PolicyMetadata $policy
    if ($readiness.Mode -ne "SAC_ENFORCED_READY") {
        throw "POLICY_INSTALL_NOT_ALLOWED_$($readiness.Mode)"
    }

    if (-not $readiness.ProjectPolicyIdentityValid -or
        -not $readiness.ProjectPolicyOnDisk -or
        -not $readiness.ProjectPolicyAuthorized -or
        -not $readiness.ProjectPolicyEnforced) {
        throw "EXISTING_PROJECT_POLICY_NOT_ELIGIBLE"
    }

    Write-Output "policy=already_active"
    Write-Output "policy_id=$($policy.PolicyId)"
    Write-Output "base_policy_id=$($policy.BasePolicyId)"
    Write-Output "reboot_required=no"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 3
}
