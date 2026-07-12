[CmdletBinding()]
param(
    [string]$Version = "0.1.0-preview.1",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\release")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$') {
    throw "Version must be a SemVer value without build metadata."
}

$versionComponents = @(
    [uint32]::Parse($Matches.major),
    [uint32]::Parse($Matches.minor),
    [uint32]::Parse($Matches.patch)
)
if ($versionComponents | Where-Object { $_ -gt 65534 }) {
    throw "Version components must be no greater than 65534 for Windows file metadata."
}
$fileVersion = "$($versionComponents[0]).$($versionComponents[1]).$($versionComponents[2]).0"

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($null -ne $dotnetCommand) {
    $dotnetCommand.Source
}
else {
    Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The .NET 8 SDK was not found."
}

$releaseName = "CodexTelegramBridge-$Version-win-x64"
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
$release = [System.IO.Path]::GetFullPath((Join-Path $resolvedOutputRoot $releaseName))
$staging = "$release.staging"
$outputPrefix = $resolvedOutputRoot + '\'
if (-not $release.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $staging.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release path escapes the requested output root."
}
if (Test-Path -LiteralPath $release) {
    throw "Release directory already exists: $release"
}
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $staging "bin") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "scripts") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "docs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "licenses") -Force | Out-Null

$commonProperties = @(
    "-c", "Release",
    "--self-contained", "true",
    "-p:RestoreLockedMode=true",
    "-p:RuntimeIdentifier=win-x64",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishTrimmed=false",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$fileVersion",
    "-p:FileVersion=$fileVersion",
    "-p:InformationalVersion=$fileVersion"
)

$bridgePublish = Join-Path $staging "publish-bridge"
$controlPublish = Join-Path $staging "publish-ctl"
& $dotnet publish (Join-Path $repo "src\CodexTelegramBridge\CodexTelegramBridge.csproj") @commonProperties -o $bridgePublish
if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed." }
& $dotnet publish (Join-Path $repo "src\CodexTelegramCtl\CodexTelegramCtl.csproj") @commonProperties -o $controlPublish
if ($LASTEXITCODE -ne 0) { throw "Control CLI publish failed." }

$bridgeExecutable = Join-Path $bridgePublish "CodexTelegramBridge.exe"
$controlExecutable = Join-Path $controlPublish "CodexTelegramCtl.exe"
if (-not (Test-Path -LiteralPath $bridgeExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $controlExecutable -PathType Leaf)) {
    throw "Published executables are missing."
}
$bundledSqlite = @(Get-ChildItem -LiteralPath $bridgePublish, $controlPublish -Filter "e_sqlite3.dll" -File -Recurse)
if ($bundledSqlite.Count -ne 0) {
    throw "The release unexpectedly contains a bundled SQLite native library; Windows winsqlite3 must be used."
}

Copy-Item -LiteralPath $bridgeExecutable -Destination (Join-Path $staging "bin\CodexTelegramBridge.exe")
Copy-Item -LiteralPath $controlExecutable -Destination (Join-Path $staging "bin\CodexTelegramCtl.exe")
Remove-Item -LiteralPath $bridgePublish -Recurse -Force
Remove-Item -LiteralPath $controlPublish -Recurse -Force

Copy-Item -LiteralPath (Join-Path $repo "scripts\install.ps1") -Destination (Join-Path $staging "scripts\install.ps1")
Copy-Item -LiteralPath (Join-Path $repo "scripts\uninstall.ps1") -Destination (Join-Path $staging "scripts\uninstall.ps1")
Copy-Item -LiteralPath (Join-Path $repo "README.md") -Destination (Join-Path $staging "README.md")
Copy-Item -LiteralPath (Join-Path $repo "LICENSE") -Destination (Join-Path $staging "LICENSE")
Copy-Item -LiteralPath (Join-Path $repo "PRIVACY.md") -Destination (Join-Path $staging "PRIVACY.md")
Copy-Item -LiteralPath (Join-Path $repo "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $staging "THIRD-PARTY-NOTICES.md")
Copy-Item -LiteralPath (Join-Path $repo "docs\code-signing-policy.md") -Destination (Join-Path $staging "docs\code-signing-policy.md")
Copy-Item -LiteralPath (Join-Path $repo "docs\validation.md") -Destination (Join-Path $staging "docs\validation.md")
Copy-Item -LiteralPath (Join-Path $repo "docs\deployment-record-template.md") -Destination (Join-Path $staging "deployment-record-template.md")

$dotnetRoot = Split-Path -Parent $dotnet
$dotnetLicense = Join-Path $dotnetRoot "LICENSE.txt"
$dotnetNotices = Join-Path $dotnetRoot "ThirdPartyNotices.txt"
if (-not (Test-Path -LiteralPath $dotnetLicense -PathType Leaf) -or
    -not (Test-Path -LiteralPath $dotnetNotices -PathType Leaf)) {
    throw "The .NET redistribution license files were not found beside dotnet.exe."
}
Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $staging "licenses\DOTNET-LICENSE.txt")
Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $staging "licenses\DOTNET-THIRD-PARTY-NOTICES.txt")

$dependencyOutput = & $dotnet list (Join-Path $repo "src\CodexTelegramCtl\CodexTelegramCtl.csproj") package --include-transitive
if ($LASTEXITCODE -ne 0) { throw "Dependency inventory generation failed." }
$dependencyOutput | Set-Content -LiteralPath (Join-Path $staging "DEPENDENCIES.txt") -Encoding UTF8

$files = Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName
$manifestLines = foreach ($file in $files) {
    $relative = $file.FullName.Substring($staging.Length).TrimStart('\').Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
[System.IO.File]::WriteAllLines(
    (Join-Path $staging "manifest.sha256"),
    [string[]]$manifestLines,
    (New-Object System.Text.UTF8Encoding($false)))

Move-Item -LiteralPath $staging -Destination $release
$outerHash = (Get-FileHash -LiteralPath (Join-Path $release "manifest.sha256") -Algorithm SHA256).Hash.ToLowerInvariant()
$outerRecord = Join-Path $resolvedOutputRoot "$releaseName.manifest.outer.sha256"
"$outerHash  $releaseName/manifest.sha256" | Set-Content -LiteralPath $outerRecord -Encoding ASCII

Write-Output "release=$release"
Write-Output "manifest_outer_sha256=$outerHash"
Write-Output "windows_file_version=$fileVersion"
Write-Output "signing_status=unsigned"
