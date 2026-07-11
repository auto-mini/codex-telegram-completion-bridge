[CmdletBinding()]
param(
    [string]$Version = "1.0.0-canary.1",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\release")
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z.-]{0,63}$') {
    throw "Version contains unsupported characters."
}
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dotnet = Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The .NET 8 SDK was not found at the expected developer path."
}

$releaseName = "CodexTelegramBridge-$Version-win-x64"
$release = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot $releaseName))
$staging = "$release.staging"
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd('\') + '\'
if (-not $release.StartsWith($resolvedOutputRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release path escapes the requested output root."
}
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $staging "bin") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "scripts") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "docs") -Force | Out-Null

$commonProperties = @(
    "-c", "Release",
    "--self-contained", "true",
    "-p:RestoreLockedMode=true",
    "-p:RuntimeIdentifier=win-x64",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishTrimmed=false"
)

& $dotnet publish (Join-Path $repo "src\CodexTelegramBridge\CodexTelegramBridge.csproj") @commonProperties -o (Join-Path $staging "publish-bridge")
if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed." }
& $dotnet publish (Join-Path $repo "src\CodexTelegramCtl\CodexTelegramCtl.csproj") @commonProperties -o (Join-Path $staging "publish-ctl")
if ($LASTEXITCODE -ne 0) { throw "Control CLI publish failed." }

Copy-Item -LiteralPath (Join-Path $staging "publish-bridge\CodexTelegramBridge.exe") -Destination (Join-Path $staging "bin\CodexTelegramBridge.exe")
Copy-Item -LiteralPath (Join-Path $staging "publish-ctl\CodexTelegramCtl.exe") -Destination (Join-Path $staging "bin\CodexTelegramCtl.exe")
Remove-Item -LiteralPath (Join-Path $staging "publish-bridge") -Recurse -Force
Remove-Item -LiteralPath (Join-Path $staging "publish-ctl") -Recurse -Force

Copy-Item -LiteralPath (Join-Path $repo "scripts\install.ps1") -Destination (Join-Path $staging "scripts\install.ps1")
Copy-Item -LiteralPath (Join-Path $repo "scripts\uninstall.ps1") -Destination (Join-Path $staging "scripts\uninstall.ps1")
Copy-Item -LiteralPath (Join-Path $repo "docs\20260711_codex_telegram_completion_notification_blueprint.md") -Destination (Join-Path $staging "docs\blueprint.md")
Copy-Item -LiteralPath (Join-Path $repo "docs\20260711_codex_telegram_completion_notification_review_loop_notes.md") -Destination (Join-Path $staging "docs\blueprint-review-loop-notes.md")
Copy-Item -LiteralPath (Join-Path $repo "README.md") -Destination (Join-Path $staging "README.md")
Copy-Item -LiteralPath (Join-Path $repo "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $staging "THIRD-PARTY-NOTICES.md")
Copy-Item -LiteralPath (Join-Path $repo "docs\deployment-record-template.md") -Destination (Join-Path $staging "deployment-record-template.md")

$dependencyOutput = & $dotnet list (Join-Path $repo "src\CodexTelegramCtl\CodexTelegramCtl.csproj") package --include-transitive
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

if (Test-Path -LiteralPath $release) {
    throw "Release directory already exists: $release"
}
Move-Item -LiteralPath $staging -Destination $release
$outerHash = (Get-FileHash -LiteralPath (Join-Path $release "manifest.sha256") -Algorithm SHA256).Hash.ToLowerInvariant()
$outerRecord = Join-Path $OutputRoot "$releaseName.manifest.outer.sha256"
"$outerHash  $releaseName/manifest.sha256" | Set-Content -LiteralPath $outerRecord -Encoding ASCII

Write-Output "release=$release"
Write-Output "manifest_outer_sha256=$outerHash"
