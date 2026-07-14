[CmdletBinding()]
param(
    [string]$FinalPackage = (Join-Path $PSScriptRoot "..\artifacts\release\CodexTelegramBridge-1.0.0-personal.1-win-x64"),
    [string]$RollbackPackage = (Join-Path $PSScriptRoot "..\artifacts\release\CodexTelegramBridge-1.0.0-canary.9-win-x64"),
    [string]$BundleVersion = "1.0.0-personal.1",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\handoff")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$commonScript = Join-Path $repo "scripts\personal-rollout\PersonalRollout.Common.ps1"
. $commonScript

function Assert-ReleasePackage {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    $root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
    $manifest = Join-Path $root "manifest.sha256"
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        throw "PACKAGE_MANIFEST_MISSING"
    }

    $expected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in [IO.File]::ReadAllLines($manifest)) {
        if ($line -notmatch '^(?<hash>[0-9a-fA-F]{64})  (?<path>[^\r\n]+)$') {
            throw "PACKAGE_MANIFEST_LINE_INVALID"
        }

        $expectedHash = $Matches.hash
        $relative = $Matches.path.Replace('\', '/')
        if (-not $expected.Add($relative)) {
            throw "PACKAGE_MANIFEST_DUPLICATE"
        }

        $path = Resolve-SafeBundlePath -BundleRoot $root -RelativePath $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "PACKAGE_FILE_MISSING"
        }

        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (-not [string]::Equals($expectedHash, $actual, [StringComparison]::OrdinalIgnoreCase)) {
            throw "PACKAGE_FILE_HASH_MISMATCH"
        }
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $root -File -Recurse -Force |
            ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\').Replace('\', '/') } |
            Where-Object { $_ -ne "manifest.sha256" }
    )
    if ($actualFiles.Count -ne $expected.Count -or @($actualFiles | Where-Object { -not $expected.Contains($_) }).Count -ne 0) {
        throw "PACKAGE_FILE_SET_MISMATCH"
    }

    $name = [IO.Path]::GetFileName($root)
    $outerPath = Join-Path (Split-Path -Parent $root) "$name.manifest.outer.sha256"
    if (-not (Test-Path -LiteralPath $outerPath -PathType Leaf)) {
        throw "PACKAGE_OUTER_MANIFEST_MISSING"
    }

    $outerLine = (Get-Content -LiteralPath $outerPath -Raw -Encoding ASCII).Trim()
    if ($outerLine -notmatch '^(?<hash>[0-9a-fA-F]{64})  (?<name>[^/]+)/manifest\.sha256$' -or
        -not [string]::Equals($Matches.name, $name, [StringComparison]::Ordinal)) {
        throw "PACKAGE_OUTER_MANIFEST_INVALID"
    }

    $outerHash = $Matches.hash
    $actualOuter = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
    if (-not [string]::Equals($outerHash, $actualOuter, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PACKAGE_OUTER_HASH_MISMATCH"
    }

    $bridge = Join-Path $root "bin\CodexTelegramBridge.exe"
    $ctl = Join-Path $root "bin\CodexTelegramCtl.exe"
    $executables = @(Get-ChildItem -LiteralPath $root -Filter "*.exe" -File -Recurse)
    if ($executables.Count -ne 2 -or
        -not (Test-Path -LiteralPath $bridge -PathType Leaf) -or
        -not (Test-Path -LiteralPath $ctl -PathType Leaf) -or
        @(Get-ChildItem -LiteralPath $root -Filter "*.dll" -File -Recurse).Count -ne 0) {
        throw "PACKAGE_EXECUTABLE_SET_INVALID"
    }

    foreach ($executable in @($bridge, $ctl)) {
        if ((Get-AuthenticodeSignature -LiteralPath $executable).Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
            throw "PACKAGE_SIGNING_STATE_UNEXPECTED"
        }
    }

    if ($name -notmatch '^CodexTelegramBridge-(?<version>.+)-win-x64$') {
        throw "PACKAGE_NAME_INVALID"
    }

    return [pscustomobject]@{
        Root = $root
        Name = $name
        Version = $Matches.version
        ManifestOuterSha256 = $actualOuter.ToLowerInvariant()
        BridgeSha256 = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash.ToLowerInvariant()
        CtlSha256 = (Get-FileHash -LiteralPath $ctl -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Save-XmlUtf8 {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $settings = New-Object Xml.XmlWriterSettings
    $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [Xml.NewLineHandling]::Replace
    $writer = [Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Xml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Assert-CiPolicySchema {
    param([Parameter(Mandatory = $true)][string]$XmlPath)

    $schemaPath = Join-Path $env:windir "schemas\CodeIntegrity\cipolicy.xsd"
    if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) {
        throw "CI_POLICY_SCHEMA_MISSING"
    }

    $settings = New-Object Xml.XmlReaderSettings
    $settings.ValidationType = [Xml.ValidationType]::Schema
    [void]$settings.Schemas.Add("urn:schemas-microsoft-com:sipolicy", $schemaPath)
    $errors = New-Object 'System.Collections.Generic.List[string]'
    $handler = [Xml.Schema.ValidationEventHandler] {
        param($sender, $eventArgs)
        $errors.Add($eventArgs.Message)
    }
    $settings.add_ValidationEventHandler($handler)
    $reader = [Xml.XmlReader]::Create($XmlPath, $settings)
    try {
        while ($reader.Read()) { }
    }
    finally {
        $reader.Dispose()
    }

    if ($errors.Count -ne 0) {
        throw "CI_POLICY_SCHEMA_VALIDATION_FAILED"
    }
}

function Write-HashManifest {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $manifest = Join-Path $rootPath "bundle.manifest.sha256"
    $outer = Join-Path $rootPath "bundle.manifest.outer.sha256"
    $lines = @(
        Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force |
            Where-Object { $_.FullName -notin @($manifest, $outer) } |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\').Replace('\', '/')
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                "$hash  $relative"
            }
    )
    [IO.File]::WriteAllLines($manifest, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
    $outerHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($outer, "$outerHash  bundle.manifest.sha256`r`n", [Text.Encoding]::ASCII)
    return $outerHash
}

if ($BundleVersion -notmatch '^[0-9A-Za-z][0-9A-Za-z.-]{0,63}$') {
    throw "BUNDLE_VERSION_INVALID"
}

$trackedChanges = @(git -C $repo status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedChanges.Count -ne 0) {
    throw "TRACKED_WORKTREE_MUST_BE_CLEAN"
}

$sourceCommit = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "SOURCE_COMMIT_UNAVAILABLE"
}

$final = Assert-ReleasePackage -PackageRoot $FinalPackage
$rollback = Assert-ReleasePackage -PackageRoot $RollbackPackage
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
$bundleName = "CodexTelegramBridge-PC2-Handoff-$BundleVersion"
$bundle = Join-Path $resolvedOutputRoot $bundleName
$staging = "$bundle.staging"
$zipPath = "$bundle.zip"
$zipHashPath = "$bundle.zip.sha256"

if ((Test-Path -LiteralPath $bundle) -or (Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $zipHashPath)) {
    throw "BUNDLE_OUTPUT_ALREADY_EXISTS"
}
if (Test-Path -LiteralPath $staging) {
    $stagingFull = [IO.Path]::GetFullPath($staging)
    $outputPrefix = $resolvedOutputRoot + '\'
    if (-not $stagingFull.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "STAGING_PATH_ESCAPE"
    }
    Remove-Item -LiteralPath $stagingFull -Recurse -Force
}

try {
    $finalDestination = Join-Path $staging "packages\final"
    $rollbackDestination = Join-Path $staging "packages\rollback"
    $policyDirectory = Join-Path $staging "policy"
    $scriptDirectory = Join-Path $staging "scripts"
    $documentDirectory = Join-Path $staging "docs"
    $scanRoot = Join-Path $staging ".policy-scan"
    foreach ($directory in @($finalDestination, $rollbackDestination, $policyDirectory, $scriptDirectory, $documentDirectory, (Join-Path $scanRoot "final"), (Join-Path $scanRoot "rollback"))) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    foreach ($item in @(Get-ChildItem -LiteralPath $final.Root -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $finalDestination -Recurse -Force
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $rollback.Root -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $rollbackDestination -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $final.Root "bin\CodexTelegramBridge.exe"), (Join-Path $final.Root "bin\CodexTelegramCtl.exe") -Destination (Join-Path $scanRoot "final")
    Copy-Item -LiteralPath (Join-Path $rollback.Root "bin\CodexTelegramBridge.exe"), (Join-Path $rollback.Root "bin\CodexTelegramCtl.exe") -Destination (Join-Path $scanRoot "rollback")

    Import-Module ConfigCI -ErrorAction Stop
    $policyName = "CodexTelegramBridge-Personal-Allow"
    $policyXml = Join-Path $policyDirectory "$policyName.xml"
    New-CIPolicy -FilePath $policyXml -Level Hash -Fallback Hash -ScanPath $scanRoot -UserPEs -MultiplePolicyFormat -NoScript -NoShadowCopy | Out-Null
    Set-CIPolicyIdInfo -FilePath $policyXml -PolicyName $policyName -PolicyId "CodexTelegramBridge-Personal-Allow-v1" -SupplementsBasePolicyID ([guid]$script:SacEnforcementBasePolicyId) | Out-Null
    foreach ($option in @(0, 3, 9, 11, 12)) {
        Set-RuleOption -FilePath $policyXml -Option $option -Delete
    }
    Set-CIPolicyVersion -FilePath $policyXml -Version "1.0.0.1" | Out-Null

    [xml]$policyDocument = Get-Content -LiteralPath $policyXml -Raw
    $namespace = New-Object Xml.XmlNamespaceManager($policyDocument.NameTable)
    $namespace.AddNamespace("c", "urn:schemas-microsoft-com:sipolicy")
    foreach ($allow in @($policyDocument.SelectNodes("//c:FileRules/c:Allow", $namespace))) {
        $friendly = [string]$allow.FriendlyName
        if ($friendly -notmatch '[\\/](?<ring>final|rollback)[\\/](?<exe>CodexTelegramBridge|CodexTelegramCtl)\.exe Hash (?<kind>Sha1|Sha256|Page Sha1|Page Sha256)$') {
            throw "POLICY_FRIENDLY_NAME_UNEXPECTED"
        }
        $allow.SetAttribute("FriendlyName", "$($Matches.ring)/$($Matches.exe).exe Hash $($Matches.kind)")
    }
    Save-XmlUtf8 -Xml $policyDocument -Path $policyXml
    Assert-CiPolicySchema -XmlPath $policyXml

    [xml]$policyDocument = Get-Content -LiteralPath $policyXml -Raw -Encoding UTF8
    $policyId = ConvertTo-NormalizedGuid $policyDocument.SiPolicy.PolicyID
    $policyCip = Join-Path $policyDirectory "{$policyId}.cip"
    ConvertFrom-CIPolicy -XmlFilePath $policyXml -BinaryFilePath $policyCip | Out-Null
    Remove-Item -LiteralPath $scanRoot -Recurse -Force

    foreach ($name in @(
        "PersonalRollout.Common.ps1",
        "Test-PersonalRolloutReadiness.ps1",
        "Install-PersonalSupplementalPolicy.ps1",
        "Remove-PersonalSupplementalPolicy.ps1",
        "Test-PersonalCandidateExecution.ps1",
        "Start-PersonalBridgeInstall.ps1",
        "Complete-PersonalBridgeInstall.ps1"
    )) {
        Copy-Item -LiteralPath (Join-Path $repo "scripts\personal-rollout\$name") -Destination (Join-Path $scriptDirectory $name)
    }
    Copy-Item -LiteralPath (Join-Path $repo "docs\pc2-handoff.md") -Destination (Join-Path $staging "START-HERE.md")
    Copy-Item -LiteralPath (Join-Path $repo "docs\personal-two-pc-rollout.md") -Destination (Join-Path $documentDirectory "personal-two-pc-rollout.md")

    $rollout = [ordered]@{
        schemaVersion = 1
        project = "CodexTelegramBridge-Personal-Rollout"
        bundleVersion = $BundleVersion
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        sourceCommit = $sourceCommit
        final = [ordered]@{
            version = $final.Version
            packageRelativePath = "packages/final"
            packageManifestOuterSha256 = $final.ManifestOuterSha256
            bridgeRelativePath = "packages/final/bin/CodexTelegramBridge.exe"
            bridgeSha256 = $final.BridgeSha256
            ctlRelativePath = "packages/final/bin/CodexTelegramCtl.exe"
            ctlSha256 = $final.CtlSha256
        }
        rollback = [ordered]@{
            version = $rollback.Version
            packageRelativePath = "packages/rollback"
            packageManifestOuterSha256 = $rollback.ManifestOuterSha256
            bridgeRelativePath = "packages/rollback/bin/CodexTelegramBridge.exe"
            bridgeSha256 = $rollback.BridgeSha256
            ctlRelativePath = "packages/rollback/bin/CodexTelegramCtl.exe"
            ctlSha256 = $rollback.CtlSha256
        }
        policy = [ordered]@{
            policyId = $policyId
            basePolicyId = $script:SacEnforcementBasePolicyId
            friendlyName = $policyName
            version = "1.0.0.1"
            xmlRelativePath = "policy/$policyName.xml"
            cipRelativePath = "policy/{$policyId}.cip"
        }
    }
    [IO.File]::WriteAllText((Join-Path $staging "rollout.json"), ($rollout | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))

    $manifestOuter = Write-HashManifest -Root $staging
    $metadataForValidation = Read-PersonalRolloutMetadata -BundleRoot $staging
    $policyForValidation = Get-PersonalPolicyXmlMetadata -BundleRoot $staging -RolloutMetadata $metadataForValidation
    $integrity = Assert-PersonalBundleIntegrity -BundleRoot $staging
    if ($policyForValidation.HashRuleCount -ne 16 -or $integrity.OuterSha256 -ne $manifestOuter) {
        throw "BUNDLE_FINAL_VALIDATION_FAILED"
    }

    $secretLikeFiles = @(
        Get-ChildItem -LiteralPath $staging -File -Recurse -Force | Where-Object {
            $_.Extension -in @(".dpapi", ".sqlite", ".log") -or $_.Name -match '(?i)credential|secret|token'
        }
    )
    if ($secretLikeFiles.Count -ne 0) {
        throw "BUNDLE_SECRET_LIKE_FILE_REJECTED"
    }

    Move-Item -LiteralPath $staging -Destination $bundle
    Compress-Archive -LiteralPath $bundle -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($zipHashPath, "$zipHash  $([IO.Path]::GetFileName($zipPath))`r`n", [Text.Encoding]::ASCII)

    Write-Output "bundle=$bundle"
    Write-Output "bundle_manifest_outer_sha256=$manifestOuter"
    Write-Output "zip=$zipPath"
    Write-Output "zip_sha256=$zipHash"
    Write-Output "policy_id=$policyId"
    Write-Output "base_policy_id=$script:SacEnforcementBasePolicyId"
}
catch {
    if (Test-Path -LiteralPath $staging) {
        $stagingFull = [IO.Path]::GetFullPath($staging)
        if ($stagingFull.StartsWith($resolvedOutputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $stagingFull -Recurse -Force
        }
    }
    throw
}
