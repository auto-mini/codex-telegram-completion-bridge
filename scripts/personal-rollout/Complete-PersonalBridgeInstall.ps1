[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PlanPath,
    [Parameter(Mandatory = $true)][string]$CtlPath,
    [Parameter(Mandatory = $true)][string]$ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Result {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][int]$ExitCode
    )

    $value = [ordered]@{
        schemaVersion = 1
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        status = $Status
        exitCode = $ExitCode
    } | ConvertTo-Json
    [IO.File]::WriteAllText($ResultPath, $value, (New-Object Text.UTF8Encoding($false)))
}

try {
    if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $CtlPath -PathType Leaf)) {
        throw "INSTALL_INPUT_MISSING"
    }

    Write-Host "Codex Telegram Bridge installation helper is ready."
    Write-Host "Close every Codex/ChatGPT desktop window. This helper does not terminate applications."
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(25)
    do {
        $running = @(
            Get-Process -ErrorAction SilentlyContinue | Where-Object {
                $_.ProcessName -in @("Codex", "ChatGPT", "codex-app-server")
            }
        )
        foreach ($process in $running) {
            $process.Dispose()
        }
        if ($running.Count -eq 0) {
            break
        }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($running.Count -ne 0) {
        Write-Result -Status "desktop_close_timeout" -ExitCode 2
        Write-Host "Timed out before Codex/ChatGPT closed. No installation was applied."
        Start-Sleep -Seconds 10
        exit 2
    }

    & $CtlPath install --apply $PlanPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Result -Status "apply_failed" -ExitCode $exitCode
        Write-Host "Installation failed with exit code $exitCode. Reopen Codex and report the result file."
        Start-Sleep -Seconds 15
        exit $exitCode
    }

    Write-Result -Status "complete" -ExitCode 0
    Write-Host "Installation complete. You can reopen Codex."
    Start-Sleep -Seconds 10
    exit 0
}
catch {
    Write-Result -Status $_.Exception.Message -ExitCode 3
    Write-Host "Installation helper failed: $($_.Exception.Message)"
    Start-Sleep -Seconds 15
    exit 3
}
