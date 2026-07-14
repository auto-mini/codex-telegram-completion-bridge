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

function Invoke-ExpectedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Path
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) {
            throw "PROCESS_START_RETURNED_FALSE"
        }

        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            throw "PROCESS_TIMEOUT"
        }

        if ($process.ExitCode -ne $ExpectedExitCode) {
            throw "PROCESS_EXIT_$($process.ExitCode)"
        }

        Write-Output "$Label=pass"
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Output "$Label`_stderr=present"
        }
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Output "$Label`_stdout=present"
        }
    }
    catch {
        throw "$Label`_FAILED_$($_.Exception.Message)"
    }
    finally {
        $process.Dispose()
    }
}

try {
    [void](Assert-PersonalBundleIntegrity -BundleRoot $BundleRoot)
    $rollout = Read-PersonalRolloutMetadata -BundleRoot $BundleRoot
    $policy = Get-PersonalPolicyXmlMetadata -BundleRoot $BundleRoot -RolloutMetadata $rollout
    Assert-IsAdministrator
    $policies = Get-CiPolicyInventory
    $readiness = Get-PersonalRolloutReadiness -Policies $policies -PolicyMetadata $policy

    if ($readiness.Mode -eq "SAC_ENFORCED_READY" -and -not $readiness.ProjectPolicyEnforced) {
        throw "PROJECT_POLICY_NOT_ACTIVE"
    }
    if ($readiness.Mode -notin @("SAC_ENFORCED_READY", "SAC_OFF_DIRECT_TEST")) {
        throw "CANDIDATE_TEST_NOT_ALLOWED_$($readiness.Mode)"
    }

    $started = Get-Date
    $executables = @(
        [pscustomobject]@{ Label = "final_bridge"; Path = Resolve-SafeBundlePath $BundleRoot ([string]$rollout.final.bridgeRelativePath); Arguments = ""; ExitCode = 2 },
        [pscustomobject]@{ Label = "final_ctl"; Path = Resolve-SafeBundlePath $BundleRoot ([string]$rollout.final.ctlRelativePath); Arguments = "help"; ExitCode = 0 },
        [pscustomobject]@{ Label = "rollback_bridge"; Path = Resolve-SafeBundlePath $BundleRoot ([string]$rollout.rollback.bridgeRelativePath); Arguments = ""; ExitCode = 2 },
        [pscustomobject]@{ Label = "rollback_ctl"; Path = Resolve-SafeBundlePath $BundleRoot ([string]$rollout.rollback.ctlRelativePath); Arguments = "help"; ExitCode = 0 }
    )

    foreach ($executable in $executables) {
        Invoke-ExpectedProcess -Path $executable.Path -Arguments $executable.Arguments -ExpectedExitCode $executable.ExitCode -Label $executable.Label
    }

    $blockEvents = @(
        Get-WinEvent -FilterHashtable @{
            LogName = "Microsoft-Windows-CodeIntegrity/Operational"
            StartTime = $started.AddSeconds(-2)
            Id = 3033, 3077
        } -ErrorAction SilentlyContinue |
            Where-Object {
                $message = [string]$_.Message
                @($executables | Where-Object { $message.IndexOf([IO.Path]::GetFileName($_.Path), [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -ne 0
            }
    )

    if ($blockEvents.Count -ne 0) {
        throw "CODE_INTEGRITY_BLOCK_EVENT_DETECTED"
    }

    Write-Output "smoke=pass"
    Write-Output "mode=$($readiness.Mode)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 3
}
