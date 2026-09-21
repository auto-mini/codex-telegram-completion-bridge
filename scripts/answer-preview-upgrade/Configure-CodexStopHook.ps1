#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$CodexPath = (Get-Command codex -ErrorAction Stop).Source,
    [string]$PowerShellPath = (Get-Command pwsh -ErrorAction Stop).Source,
    [string]$VerificationCwd = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexHook.Common.ps1')
foreach ($executable in @($CodexPath, $PowerShellPath)) {
    if (-not [IO.Path]::IsPathFullyQualified($executable) -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'EXECUTABLE_PATH_INVALID'
    }
}
$installed = Join-Path $env:LOCALAPPDATA 'CodexTelegramBridge'
$bridge = Join-Path $installed 'bin\CodexTelegramBridge.exe'
if (-not (Test-Path -LiteralPath $bridge -PathType Leaf) -or -not (Get-Acl -LiteralPath $installed).AreAccessRulesProtected) {
    throw 'PROTECTED_EXISTING_INSTALLATION_REQUIRED'
}
$adapterSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Invoke-CodexStopHook.ps1'))
$adapter = Join-Path $installed 'config\Invoke-CodexStopHook.ps1'
$recordPath = Join-Path $installed 'config\stdin-stop-hook.json'
$hookCommand = Get-CodexStopHookCommand -PowerShellPath $PowerShellPath -AdapterPath $adapter -BridgePath $bridge
$listed = Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'hooks/list' -Params @{cwds=@($VerificationCwd)}
if (@($listed.data[0].errors).Count -ne 0) { throw 'EXISTING_HOOK_CONFIG_ERRORS' }

if (Test-Path -LiteralPath $adapter) {
    if ((Get-FileHash -LiteralPath $adapter -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $adapterSource -Algorithm SHA256).Hash) {
        throw 'EXISTING_ADAPTER_DIFFERS_REVIEW_REQUIRED'
    }
    $owned = @($listed.data[0].hooks | Where-Object {
        $_.eventName -eq 'stop' -and $_.handlerType -eq 'command' -and ($_.command -ceq $hookCommand -or $_.command.Contains($adapter))
    })
    if ($owned.Count -eq 1 -and $owned[0].enabled -and $owned[0].trustStatus -eq 'trusted') {
        Write-Output 'stdin_stop_hook=already_configured_and_trusted'
        exit 0
    }
    throw 'EXISTING_ADAPTER_STATE_REQUIRES_REVIEW'
}

$configuration = Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'config/read' -Params @{includeLayers=$true}
$layers = @($configuration.layers | Where-Object { $_.name.type -eq 'user' -and $null -eq $_.name.profile })
if ($layers.Count -ne 1) { throw 'USER_CONFIG_LAYER_AMBIGUOUS' }
$layer = $layers[0]
$configPath = $layer.name.file
$backup = Join-Path $installed ('backups\stdin-stop-hook-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$bytes = [IO.File]::ReadAllBytes($configPath)
try {
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes, [Text.Encoding]::UTF8.GetBytes('CodexTelegramBridge:stop-hook-config:v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $backup 'codex-config-before.dpapi'), $encrypted)
} finally { [Array]::Clear($bytes, 0, $bytes.Length) }
Copy-Item -LiteralPath $adapterSource -Destination $adapter

$groups = @()
if ($layer.config.ContainsKey('hooks') -and $layer.config.hooks.ContainsKey('Stop')) {
    $groups = @($layer.config.hooks.Stop)
}
$groups += @{hooks=@(@{type='command'; command=$hookCommand; timeout=20; statusMessage='Capture Telegram completion'})}
[void](Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'config/value/write' -Params @{
    keyPath='hooks.Stop'; mergeStrategy='replace'; value=$groups; filePath=$configPath; expectedVersion=$layer.version
})
$after = Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'hooks/list' -Params @{cwds=@($VerificationCwd)}
$selected = @($after.data[0].hooks | Where-Object { $_.eventName -eq 'stop' -and $_.handlerType -eq 'command' -and $_.command -ceq $hookCommand })
if ($selected.Count -ne 1 -or @($after.data[0].errors).Count -ne 0) { throw 'STOP_HOOK_REGISTRATION_INVALID' }
$hook = $selected[0]
[ordered]@{key=$hook.key; current_hash=$hook.currentHash; command=$hookCommand; adapter=$adapter; config_path=$configPath; backup=$backup; backup_format='raw-config-dpapi'; backup_entropy='CodexTelegramBridge:stop-hook-config:v1'} |
    ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding UTF8

# Trust only the reviewed definition hash, preserving every other hook/state.
$fresh = Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'config/read' -Params @{includeLayers=$true}
$freshLayer = @($fresh.layers | Where-Object { $_.name.type -eq 'user' -and $null -eq $_.name.profile })[0]
[void](Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'config/value/write' -Params @{
    keyPath=('hooks.state.' + ($hook.key | ConvertTo-Json -Compress)); mergeStrategy='replace'
    value=@{enabled=$true; trusted_hash=$hook.currentHash}; filePath=$configPath; expectedVersion=$freshLayer.version
})
$verified = Invoke-CodexLocalRpc -CodexPath $CodexPath -Method 'hooks/list' -Params @{cwds=@($VerificationCwd)}
$verifiedHook = @($verified.data[0].hooks | Where-Object { $_.key -ceq $hook.key })
if ($verifiedHook.Count -ne 1 -or -not $verifiedHook[0].enabled -or $verifiedHook[0].trustStatus -ne 'trusted') {
    throw 'STOP_HOOK_TRUST_NOT_ACTIVE'
}
Write-Output 'stdin_stop_hook=configured_and_trusted'
Write-Output "stdin_stop_hook_record=$recordPath"
Write-Output 'next_action=create_fresh_upgrade_plan_then_restart_desktop_after_installation'
