$ErrorActionPreference = 'Stop'
$stage = $PSScriptRoot
$task = Get-Content -LiteralPath (Join-Path $stage 'scheduled-task.json') -Raw | ConvertFrom-Json
$code = 3
try {
    [ordered]@{status='waiting_for_desktop_close';preview_characters=50;launcher='windows_task_scheduler';pid=$PID;started_utc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'verification.json') -Encoding UTF8
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $shell -NoLogo -NoProfile -NonInteractive -File (Join-Path $stage 'Apply-And-Verify.ps1') *> (Join-Path $stage 'scheduled-runner.log')
    $code = $LASTEXITCODE
} catch {
    [ordered]@{status='scheduled_runner_failed';error_type=$_.Exception.GetType().Name;completed_utc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'verification.json') -Encoding UTF8
} finally {
    Unregister-ScheduledTask -TaskPath $task.task_path -TaskName $task.task_name -Confirm:$false -ErrorAction SilentlyContinue
}
exit $code
