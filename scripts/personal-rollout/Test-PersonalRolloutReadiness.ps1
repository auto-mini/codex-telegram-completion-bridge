[CmdletBinding()]
param(
    [string]$BundleRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $BundleRoot = Join-Path $PSScriptRoot ".."
}
. (Join-Path $PSScriptRoot "PersonalRollout.Common.ps1")

try {
    $integrity = Assert-PersonalBundleIntegrity -BundleRoot $BundleRoot
    $rollout = Read-PersonalRolloutMetadata -BundleRoot $BundleRoot
    $policy = Get-PersonalPolicyXmlMetadata -BundleRoot $BundleRoot -RolloutMetadata $rollout
    Assert-IsAdministrator
    $policies = Get-CiPolicyInventory
    $readiness = Get-PersonalRolloutReadiness -Policies $policies -PolicyMetadata $policy

    Write-Output "integrity=pass"
    Write-Output "bundle_files=$($integrity.FileCount)"
    Write-Output "bundle_manifest_sha256=$($integrity.OuterSha256)"
    Write-Output "mode=$($readiness.Mode)"
    Write-Output "windows=$($readiness.ProductName); edition=$($readiness.EditionId); display=$($readiness.DisplayVersion); build=$($readiness.Build)"
    Write-Output "smart_app_control_state=$($readiness.SmartAppControlState)"
    Write-Output "sac_base_active=$($readiness.SacBaseActive.ToString().ToLowerInvariant())"
    Write-Output "project_policy_present=$($readiness.ProjectPolicyPresent.ToString().ToLowerInvariant())"
    Write-Output "project_policy_enforced=$($readiness.ProjectPolicyEnforced.ToString().ToLowerInvariant())"
    Write-Output "extra_base_policy_count=$($readiness.ExtraBasePolicyCount)"
    Write-Output "reasons=$(if ($readiness.Reasons.Count -eq 0) { 'none' } else { $readiness.Reasons -join ',' })"

    if ($readiness.Mode -eq "BLOCKED") {
        exit 2
    }

    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 3
}
