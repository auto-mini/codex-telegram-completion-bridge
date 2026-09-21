#requires -Version 7.2

function Get-CodexStopHookCommand {
    param([string]$PowerShellPath, [string]$AdapterPath, [string]$BridgePath)
    $windowsShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ($windowsShell -match '[\s"&|<>]') { throw 'WINDOWS_SHELL_PATH_UNSUPPORTED' }
    $invocation = "& '" + $PowerShellPath.Replace("'", "''") +
        "' -NoLogo -NoProfile -NonInteractive -File '" + $AdapterPath.Replace("'", "''") +
        "' -BridgePath '" + $BridgePath.Replace("'", "''") + "'; exit `$LASTEXITCODE"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($invocation))
    return "$windowsShell -NoLogo -NoProfile -NonInteractive -EncodedCommand $encoded"
}

function Invoke-CodexLocalRpc {
    param([string]$Method, [hashtable]$Params, [string]$CodexPath = (Get-Command codex -ErrorAction Stop).Source)
    $start = [Diagnostics.ProcessStartInfo]::new($CodexPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($arg in @('app-server','--stdio')) { $start.ArgumentList.Add($arg) }
    $p = [Diagnostics.Process]::Start($start)
    $errors = $p.StandardError.ReadToEndAsync()
    function Read-RpcResponse([int]$Id) {
        for ($n=0; $n -lt 100; $n++) {
            $lineTask = $p.StandardOutput.ReadLineAsync()
            if (-not $lineTask.Wait(15000)) { throw 'LOCAL_RPC_TIMEOUT' }
            $line = $lineTask.Result
            if ($null -eq $line) { throw 'LOCAL_RPC_CLOSED' }
            $item = $line | ConvertFrom-Json -AsHashtable
            if ($item.ContainsKey('id') -and $item.id -eq $Id) {
                if ($item.ContainsKey('error')) { throw ('LOCAL_RPC_ERROR_' + $item.error.code + ': ' + $item.error.message) }
                return $item.result
            }
        }
        throw 'LOCAL_RPC_RESPONSE_NOT_FOUND'
    }
    try {
        $p.StandardInput.WriteLine((@{id=1;method='initialize';params=@{clientInfo=@{name='codex_telegram_bridge_setup';version='1.0.1'};capabilities=@{experimentalApi=$true}}} | ConvertTo-Json -Depth 8 -Compress))
        [void](Read-RpcResponse 1)
        $p.StandardInput.WriteLine('{"method":"initialized"}')
        $p.StandardInput.WriteLine((@{id=2;method=$Method;params=$Params} | ConvertTo-Json -Depth 20 -Compress))
        return Read-RpcResponse 2
    } finally {
        try { $p.StandardInput.Close() } catch {}
        if (-not $p.WaitForExit(1000)) { $p.Kill() }
        $p.Dispose()
    }
}
