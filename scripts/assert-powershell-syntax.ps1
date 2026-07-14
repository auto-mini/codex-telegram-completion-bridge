[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$failures = New-Object 'System.Collections.Generic.List[string]'
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $root "scripts") -Filter "*.ps1" -File -Recurse)) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    foreach ($error in @($errors)) {
        $failures.Add("$($file.FullName):$($error.Extent.StartLineNumber): $($error.Message)")
    }
}

if ($failures.Count -ne 0) {
    throw "PowerShell parse failures:`n$($failures -join [Environment]::NewLine)"
}

Write-Output "powershell_syntax=pass"
