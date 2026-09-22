#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'CodexTelegramBridge'
$ctl = Join-Path $root 'bin\CodexTelegramCtl.exe'
$doctor = (& $ctl doctor --online --json | Out-String) | ConvertFrom-Json
$doctorExit = $LASTEXITCODE
$versions = foreach ($name in @('CodexTelegramBridge.exe','CodexTelegramCtl.exe')) {
    $path = Join-Path $root ('bin\' + $name)
    [ordered]@{name=$name; version=(Get-Item -LiteralPath $path).VersionInfo.FileVersion; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
$allProcesses = @(Get-CimInstance Win32_Process)
$npmRoot = (Join-Path $env:APPDATA 'npm\node_modules\@openai\codex') + [IO.Path]::DirectorySeparatorChar
$desktopRoot = (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') + [IO.Path]::DirectorySeparatorChar
$processes = foreach ($process in $allProcesses | Where-Object { $_.Name -in @('Codex.exe','ChatGPT.exe','codex-app-server.exe') }) {
    $path = [string]$process.ExecutablePath
    $kind = if ($path.StartsWith($npmRoot,[StringComparison]::OrdinalIgnoreCase)) {'npm_cli'} elseif ($path.StartsWith($desktopRoot,[StringComparison]::OrdinalIgnoreCase)) {'bundled_cli'} else {'desktop_or_unknown'}
    $parent = @($allProcesses | Where-Object { $_.ProcessId -eq $process.ParentProcessId } | Select-Object -First 1)
    [ordered]@{name=$process.Name; kind=$kind; parent_name=if($parent.Count){$parent[0].Name}else{$null}}
}
$stages = foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $root 'backups') -Directory | Where-Object { $_.Name -like 'answer-preview-50*' } | Sort-Object LastWriteTime -Descending | Select-Object -First 3) {
    $entry = [ordered]@{created_utc=$directory.CreationTimeUtc.ToString('O')}
    foreach ($name in @('verification.json','install-result.json')) {
        $path = Join-Path $directory.FullName $name
        if (Test-Path -LiteralPath $path) {
            $value = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $entry[$name] = [ordered]@{status=$value.status; error_type=$value.error_type; exitCode=$value.exitCode; version=$value.version}
        }
    }
    $entry
}
$tasks = foreach ($task in Get-ScheduledTask -TaskPath '\CodexTelegramBridge\') {
    $info = Get-ScheduledTaskInfo -TaskPath $task.TaskPath -TaskName $task.TaskName
    [ordered]@{name=$task.TaskName; state=[string]$task.State; last_result=$info.LastTaskResult}
}
$command = Get-Command codex -ErrorAction SilentlyContinue
$report = [ordered]@{
    schema_version=1; observed_utc=[DateTimeOffset]::UtcNow.ToString('O'); versions=@($versions)
    doctor_exit=$doctorExit; overall=$doctor.overall; conditions=$doctor.conditions
    telegram_online=$doctor.checks.telegram_online; capture_mode=$doctor.capture_mode; delivery_paused=$doctor.delivery_paused
    processes=@($processes); stages=@($stages); tasks=@($tasks)
    codex_command_extension=if($command){[IO.Path]::GetExtension($command.Source)}else{$null}
}
# Deliberately excludes paths, PC/user names, SIDs, credentials and message content.
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output 'deployment_state_written=yes'
