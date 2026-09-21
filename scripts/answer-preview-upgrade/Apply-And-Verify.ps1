$ErrorActionPreference = 'Stop'
$stage = $PSScriptRoot
$resultPath = Join-Path $stage 'verification.json'
try {
    $planPath = Join-Path $stage 'install-plan.json'
    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    $before = Get-Content -LiteralPath (Join-Path $stage 'before.json') -Raw | ConvertFrom-Json
    $ctl = Join-Path $plan.package_root 'bin\CodexTelegramCtl.exe'
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $shell -NoLogo -NoProfile -File (Join-Path $stage 'Complete-PersonalBridgeInstall.ps1') -PlanPath $planPath -CtlPath $ctl -ResultPath (Join-Path $stage 'install-result.json') *> (Join-Path $stage 'installer.log')
    if ($LASTEXITCODE -ne 0) { throw 'INSTALL_NOT_COMPLETED' }
    $installedCtl = Join-Path $plan.installation_root 'bin\CodexTelegramCtl.exe'
    $after = Get-Content -LiteralPath (Join-Path $plan.installation_root 'config\bridge.json') -Raw | ConvertFrom-Json
    foreach ($field in @('machine_id','codex_home','pc_alias','capture_mode','delivery_paused','auto_repair_vendor_notify')) {
        if ($before.runtime.$field -cne $after.$field) { throw 'RUNTIME_PRESERVATION_CHECK_FAILED' }
    }
    $credentialsHash = (Get-FileHash -LiteralPath (Join-Path $plan.installation_root 'config\telegram-credentials.dpapi') -Algorithm SHA256).Hash
    if ($before.credentials_sha256 -cne $credentialsHash) { throw 'CREDENTIAL_PRESERVATION_CHECK_FAILED' }
    foreach ($name in @('CodexTelegramBridge.exe','CodexTelegramCtl.exe')) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $plan.package_root ('bin\' + $name)) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $plan.installation_root ('bin\' + $name)) -Algorithm SHA256).Hash
        if ($expected -cne $actual) { throw 'INSTALLED_BINARY_HASH_MISMATCH' }
    }
    $doctorJson = & $installedCtl doctor --online --json
    $doctorExit = $LASTEXITCODE
    $doctorJson | Set-Content -LiteralPath (Join-Path $stage 'doctor-after.json') -Encoding UTF8
    $doctor = $doctorJson | ConvertFrom-Json
    $unexpected = @($doctor.conditions | Where-Object { $_ -ne 'EVENT_QUARANTINED' })
    if ($doctorExit -notin @(0,1) -or $unexpected.Count -gt 0 -or $doctor.checks.telegram_online -ne 'OK' -or $doctor.checks.runtime_config -ne 'OK' -or $doctor.checks.package_manifest -notlike 'OK_*') { throw 'POST_INSTALL_DIAGNOSTIC_FAILED' }
    [ordered]@{status='installed_and_verified';version='1.0.1.0';credentials_preserved=$true;runtime_preserved=$true;telegram_online='OK';existing_quarantine_preserved=$true;completed_utc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
} catch {
    [ordered]@{status='needs_attention';error_type=$_.Exception.GetType().Name;completed_utc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    exit 3
}
