[CmdletBinding()]
param(
    [string]$Solution = (Join-Path $PSScriptRoot "..\CodexTelegramBridge.sln")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($null -ne $dotnetCommand) {
    $dotnetCommand.Source
}
else {
    Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The .NET SDK was not found."
}

$jsonLines = & $dotnet list ([System.IO.Path]::GetFullPath($Solution)) package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability audit failed to run."
}

$audit = ($jsonLines -join [Environment]::NewLine) | ConvertFrom-Json
$findings = @(
    foreach ($project in @($audit.projects)) {
        $frameworksProperty = $project.PSObject.Properties['frameworks']
        if ($null -eq $frameworksProperty) {
            continue
        }
        foreach ($framework in @($frameworksProperty.Value)) {
            $topLevelProperty = $framework.PSObject.Properties['topLevelPackages']
            $transitiveProperty = $framework.PSObject.Properties['transitivePackages']
            $packages = @()
            if ($null -ne $topLevelProperty) { $packages += @($topLevelProperty.Value) }
            if ($null -ne $transitiveProperty) { $packages += @($transitiveProperty.Value) }
            foreach ($package in $packages) {
                $vulnerabilitiesProperty = $package.PSObject.Properties['vulnerabilities']
                if ($null -eq $vulnerabilitiesProperty) {
                    continue
                }
                foreach ($vulnerability in @($vulnerabilitiesProperty.Value)) {
                    if ($null -ne $vulnerability) {
                        [pscustomobject]@{
                            Package = $package.id
                            Version = $package.resolvedVersion
                            Severity = $vulnerability.severity
                            Advisory = $vulnerability.advisoryurl
                        }
                    }
                }
            }
        }
    }
)

if ($findings.Count -ne 0) {
    $summary = $findings |
        Sort-Object Package, Version, Advisory -Unique |
        ForEach-Object { "$($_.Package) $($_.Version) [$($_.Severity)] $($_.Advisory)" }
    throw "Known vulnerable NuGet packages were found:`n$($summary -join [Environment]::NewLine)"
}

Write-Output "nuget_vulnerability_audit=clean"
