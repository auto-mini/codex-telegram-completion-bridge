[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dotnet = Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The .NET 8 SDK was not found at the expected developer path."
}

& $dotnet restore (Join-Path $repo "tools\SigningTools\SigningTools.csproj") --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "The pinned Microsoft signing tools restore failed."
}

. (Join-Path $PSScriptRoot "AuthenticodeSigning.ps1")
$signTool = Resolve-SignTool
Write-Output "signing_tools=ready"
Write-Output "signtool=$signTool"
