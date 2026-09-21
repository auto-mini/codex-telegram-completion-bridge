[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageRoot,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedManifestSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifest = Join-Path $package 'manifest.sha256'
if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ine $ExpectedManifestSha256) {
    throw 'PACKAGE_MANIFEST_HASH_MISMATCH'
}

# The operator must first check the exact downloaded ZIP hash and this PC's
# Windows execution policy. This helper never changes any security policy.
$installed = Join-Path $env:LOCALAPPDATA 'CodexTelegramBridge'
$runtimePath = Join-Path $installed 'config\bridge.json'
$credentialsPath = Join-Path $installed 'config\telegram-credentials.dpapi'
if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $credentialsPath -PathType Leaf)) {
    throw 'EXISTING_CONFIGURED_INSTALLATION_REQUIRED'
}
$acl = Get-Acl -LiteralPath $installed
if (-not $acl.AreAccessRulesProtected) { throw 'PROTECTED_INSTALLATION_ROOT_REQUIRED' }

$stage = Join-Path $installed ('backups\answer-preview-50-upgrade-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in @('Apply-And-Verify.ps1', 'Run-ScheduledUpgrade.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\personal-rollout\Complete-PersonalBridgeInstall.ps1') -Destination (Join-Path $stage 'Complete-PersonalBridgeInstall.ps1')

$ctl = Join-Path $package 'bin\CodexTelegramCtl.exe'
$planPath = Join-Path $stage 'install-plan.json'
& $ctl install --plan $planPath --package-root $package
if ($LASTEXITCODE -ne 0) { throw 'INSTALL_PLAN_FAILED' }
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
if ($plan.notify_classification -ne 'healthy_bridge' -or
    [IO.Path]::GetFullPath($plan.installation_root).TrimEnd('\') -ine [IO.Path]::GetFullPath($installed).TrimEnd('\')) {
    throw 'HEALTHY_EXISTING_INSTALLATION_REQUIRED'
}

$before = [ordered]@{
    runtime = (Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json)
    credentials_sha256 = (Get-FileHash -LiteralPath $credentialsPath -Algorithm SHA256).Hash
}
$before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'before.json') -Encoding UTF8

$taskPath = '\CodexTelegramBridge\'
if (@(Get-ScheduledTask -TaskPath $taskPath -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'UpgradeAnswerPreview50-*' }).Count -gt 0) {
    throw 'UPGRADE_TASK_ALREADY_EXISTS_REVIEW_BEFORE_RETRY'
}
$taskName = 'UpgradeAnswerPreview50-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
[ordered]@{task_name=$taskName; task_path=$taskPath; created_utc=[DateTimeOffset]::UtcNow.ToString('O')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'scheduled-task.json') -Encoding UTF8

$shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $shell -Argument ('-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "' + (Join-Path $stage 'Run-ScheduledUpgrade.ps1') + '"') -WorkingDirectory $stage
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$principal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 35) -MultipleInstances IgnoreNew -Hidden
Register-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Description 'Install the authorized 50-character answer preview upgrade after desktop shutdown, verify, then remove this one-time task.' | Out-Null
Start-ScheduledTask -TaskPath $taskPath -TaskName $taskName

Write-Output "upgrade_stage=$stage"
Write-Output "upgrade_task=$taskName"
Write-Output 'next_action=verify_task_is_running_then_close_Codex_and_ChatGPT_within_25_minutes'
Write-Output 'after_restart=read_verification.json_and_run_installed_doctor'
