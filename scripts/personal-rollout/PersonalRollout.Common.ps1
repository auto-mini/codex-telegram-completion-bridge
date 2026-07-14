Set-StrictMode -Version Latest

$script:PersonalRolloutSchemaVersion = 1
$script:PersonalRolloutProject = "CodexTelegramBridge-Personal-Rollout"
$script:SacEnforcementBasePolicyId = "0283AC0F-FFF1-49AE-ADA1-8A933130CAD6"
$script:SacEnforcementFriendlyName = "VerifiedAndReputableDesktop"

function ConvertTo-NormalizedGuid {
    param([Parameter(Mandatory = $true)][object]$Value)

    try {
        return ([guid]([string]$Value)).ToString("D").ToUpperInvariant()
    }
    catch {
        throw "INVALID_GUID"
    }
}

function ConvertTo-StrictBoolean {
    param([Parameter(Mandatory = $true)][object]$Value)

    if ($Value -is [bool]) {
        return $Value
    }

    if ([string]::Equals([string]$Value, "true", [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ([string]::Equals([string]$Value, "false", [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    throw "INVALID_BOOLEAN"
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-IsAdministrator {
    if (-not (Test-IsAdministrator)) {
        throw "ADMINISTRATOR_REQUIRED"
    }
}

function Resolve-SafeBundlePath {
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.IndexOf([char]0) -ge 0) {
        throw "BUNDLE_PATH_INVALID"
    }

    $root = [IO.Path]::GetFullPath($BundleRoot).TrimEnd('\')
    $normalizedRelative = $RelativePath.Replace('/', '\')
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $normalizedRelative))
    $prefix = $root + '\'
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "BUNDLE_PATH_ESCAPE"
    }

    return $candidate
}

function Read-PersonalRolloutMetadata {
    param([Parameter(Mandatory = $true)][string]$BundleRoot)

    $path = Join-Path ([IO.Path]::GetFullPath($BundleRoot)) "rollout.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "ROLLOUT_METADATA_MISSING"
    }

    try {
        $metadata = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "ROLLOUT_METADATA_INVALID"
    }

    if ($metadata.schemaVersion -ne $script:PersonalRolloutSchemaVersion -or
        -not [string]::Equals([string]$metadata.project, $script:PersonalRolloutProject, [StringComparison]::Ordinal)) {
        throw "ROLLOUT_METADATA_SCHEMA_UNSUPPORTED"
    }

    foreach ($required in @("final", "rollback", "policy")) {
        if ($null -eq $metadata.$required) {
            throw "ROLLOUT_METADATA_INCOMPLETE"
        }
    }

    return $metadata
}

function Assert-PersonalBundleIntegrity {
    param([Parameter(Mandatory = $true)][string]$BundleRoot)

    $root = [IO.Path]::GetFullPath($BundleRoot).TrimEnd('\')
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint -or
        @(Get-ChildItem -LiteralPath $root -Directory -Recurse -Force | Where-Object {
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint
        }).Count -ne 0) {
        throw "BUNDLE_REPARSE_POINT_REJECTED"
    }

    $manifestPath = Join-Path $root "bundle.manifest.sha256"
    $outerPath = Join-Path $root "bundle.manifest.outer.sha256"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $outerPath -PathType Leaf)) {
        throw "BUNDLE_MANIFEST_MISSING"
    }

    $outerLine = (Get-Content -LiteralPath $outerPath -Raw -Encoding ASCII).Trim()
    if ($outerLine -notmatch '^(?<hash>[0-9a-fA-F]{64})  bundle\.manifest\.sha256$') {
        throw "BUNDLE_OUTER_MANIFEST_INVALID"
    }

    $actualOuter = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    if (-not [string]::Equals($Matches.hash, $actualOuter, [StringComparison]::OrdinalIgnoreCase)) {
        throw "BUNDLE_OUTER_HASH_MISMATCH"
    }

    $expected = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in [IO.File]::ReadAllLines($manifestPath)) {
        if ($line -notmatch '^(?<hash>[0-9a-fA-F]{64})  (?<path>[^\r\n]+)$') {
            throw "BUNDLE_MANIFEST_LINE_INVALID"
        }

        $relative = $Matches.path.Replace('\', '/')
        if ($relative -in @("bundle.manifest.sha256", "bundle.manifest.outer.sha256") -or $expected.ContainsKey($relative)) {
            throw "BUNDLE_MANIFEST_ENTRY_INVALID"
        }

        $path = Resolve-SafeBundlePath -BundleRoot $root -RelativePath $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "BUNDLE_FILE_MISSING"
        }

        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "BUNDLE_REPARSE_POINT_REJECTED"
        }

        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (-not [string]::Equals($Matches.hash, $actual, [StringComparison]::OrdinalIgnoreCase)) {
            throw "BUNDLE_FILE_HASH_MISMATCH"
        }

        $expected.Add($relative, $actual.ToLowerInvariant())
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $root -File -Recurse -Force |
            ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\').Replace('\', '/') } |
            Where-Object { $_ -notin @("bundle.manifest.sha256", "bundle.manifest.outer.sha256") }
    )
    if ($actualFiles.Count -ne $expected.Count) {
        throw "BUNDLE_FILE_SET_MISMATCH"
    }

    foreach ($relative in $actualFiles) {
        if (-not $expected.ContainsKey($relative)) {
            throw "BUNDLE_UNMANIFESTED_FILE"
        }
    }

    return [pscustomobject]@{
        FileCount = $expected.Count
        OuterSha256 = $actualOuter.ToLowerInvariant()
    }
}

function Get-PersonalPolicyXmlMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][object]$RolloutMetadata
    )

    $xmlPath = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$RolloutMetadata.policy.xmlRelativePath)
    $cipPath = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$RolloutMetadata.policy.cipRelativePath)
    if (-not (Test-Path -LiteralPath $xmlPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $cipPath -PathType Leaf)) {
        throw "POLICY_FILE_MISSING"
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
    }
    catch {
        throw "POLICY_XML_INVALID"
    }

    $namespace = New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $namespace.AddNamespace("c", "urn:schemas-microsoft-com:sipolicy")
    $policyId = ConvertTo-NormalizedGuid $xml.SiPolicy.PolicyID
    $basePolicyId = ConvertTo-NormalizedGuid $xml.SiPolicy.BasePolicyID
    $expectedPolicyId = ConvertTo-NormalizedGuid $RolloutMetadata.policy.policyId
    $expectedBasePolicyId = ConvertTo-NormalizedGuid $RolloutMetadata.policy.basePolicyId

    if (-not [string]::Equals([string]$xml.SiPolicy.PolicyType, "Supplemental Policy", [StringComparison]::Ordinal) -or
        $policyId -ne $expectedPolicyId -or
        $basePolicyId -ne $expectedBasePolicyId -or
        $basePolicyId -ne $script:SacEnforcementBasePolicyId -or
        $policyId -eq $basePolicyId -or
        -not [string]::Equals([string]$xml.SiPolicy.VersionEx, [string]$RolloutMetadata.policy.version, [StringComparison]::Ordinal)) {
        throw "POLICY_IDENTITY_MISMATCH"
    }

    $nameSetting = $xml.SelectSingleNode("//c:Setting[@Provider='PolicyInfo' and @Key='Information' and @ValueName='Name']/c:Value/c:String", $namespace)
    if ($null -eq $nameSetting -or
        -not [string]::Equals([string]$nameSetting.InnerText, [string]$RolloutMetadata.policy.friendlyName, [StringComparison]::Ordinal)) {
        throw "POLICY_FRIENDLY_NAME_MISMATCH"
    }

    $expectedCipName = "{$policyId}.cip"
    if (-not [string]::Equals([IO.Path]::GetFileName($cipPath), $expectedCipName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "POLICY_BINARY_NAME_MISMATCH"
    }

    $options = @($xml.SelectNodes("//c:Rules/c:Rule/c:Option", $namespace) | ForEach-Object { $_.'#text' })
    if ($options.Count -ne 1 -or $options[0] -ne "Enabled:Unsigned System Integrity Policy") {
        throw "POLICY_OPTIONS_TOO_BROAD"
    }

    if (@($xml.SelectNodes("//c:Signers/c:Signer", $namespace)).Count -ne 0 -or
        @($xml.SelectNodes("//c:UpdatePolicySigners/*", $namespace)).Count -ne 0 -or
        @($xml.SelectNodes("//c:SupplementalPolicySigners/*", $namespace)).Count -ne 0 -or
        @($xml.SelectNodes("//*[local-name()='Deny']", $namespace)).Count -ne 0 -or
        @($xml.SelectNodes("//*[local-name()='FileAttrib']", $namespace)).Count -ne 0 -or
        @($xml.SelectNodes("//*[@FilePath or @FileName or @PublisherName]", $namespace)).Count -ne 0) {
        throw "POLICY_NON_HASH_RULE_REJECTED"
    }

    $allows = @($xml.SelectNodes("//c:FileRules/c:Allow", $namespace))
    if ($allows.Count -ne 16 -or @($xml.SelectNodes("//c:FileRules/*", $namespace)).Count -ne $allows.Count) {
        throw "POLICY_HASH_RULE_COUNT_INVALID"
    }

    foreach ($ring in @("final", "rollback")) {
        $payload = $RolloutMetadata.$ring
        $packageRoot = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$payload.packageRelativePath)
        $manifestPath = Join-Path $packageRoot "manifest.sha256"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
            [string]$payload.packageManifestOuterSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            -not [string]::Equals([string]$payload.packageManifestOuterSha256, (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "ROLLOUT_PACKAGE_MANIFEST_MISMATCH"
        }

        $expectedManifestEntries = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($kind in @("bridge", "ctl")) {
            $relativeProperty = "$kind`RelativePath"
            $hashProperty = "$kind`Sha256"
            $payloadPath = Resolve-SafeBundlePath -BundleRoot $BundleRoot -RelativePath ([string]$payload.$relativeProperty)
            $expectedName = if ($kind -eq "bridge") { "CodexTelegramBridge.exe" } else { "CodexTelegramCtl.exe" }
            $expectedPath = Join-Path $packageRoot "bin\$expectedName"
            if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf) -or
                -not [string]::Equals($payloadPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase) -or
                [string]$payload.$hashProperty -notmatch '^[0-9a-fA-F]{64}$' -or
                -not [string]::Equals([string]$payload.$hashProperty, (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash, [StringComparison]::OrdinalIgnoreCase)) {
                throw "ROLLOUT_EXECUTABLE_METADATA_MISMATCH"
            }
            [void]$expectedManifestEntries.Add("$(([string]$payload.$hashProperty).ToLowerInvariant())  bin/$expectedName")
        }

        $manifestEntries = @([IO.File]::ReadAllLines($manifestPath))
        if ($manifestEntries.Count -ne 2 -or
            @($manifestEntries | Where-Object { -not $expectedManifestEntries.Remove($_) }).Count -ne 0 -or
            $expectedManifestEntries.Count -ne 0) {
            throw "ROLLOUT_PACKAGE_MANIFEST_CONTENT_INVALID"
        }

        $packageFiles = @(
            Get-ChildItem -LiteralPath $packageRoot -File -Recurse -Force |
                ForEach-Object { $_.FullName.Substring($packageRoot.Length).TrimStart('\').Replace('\', '/') }
        )
        if ($packageFiles.Count -ne 3 -or
            @($packageFiles | Where-Object { $_ -notin @("manifest.sha256", "bin/CodexTelegramBridge.exe", "bin/CodexTelegramCtl.exe") }).Count -ne 0) {
            throw "ROLLOUT_PACKAGE_NOT_MINIMAL"
        }
    }

    $expectedFriendlyNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($ring in @("final", "rollback")) {
        foreach ($executable in @("CodexTelegramBridge", "CodexTelegramCtl")) {
            foreach ($kind in @("Sha1", "Sha256", "Page Sha1", "Page Sha256")) {
                [void]$expectedFriendlyNames.Add("$ring/$executable.exe Hash $kind")
            }
        }
    }

    $ruleIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($allow in $allows) {
        $friendlyName = [string]$allow.FriendlyName
        if (-not $expectedFriendlyNames.Remove($friendlyName) -or -not $ruleIds.Add([string]$allow.ID)) {
            throw "POLICY_HASH_RULE_IDENTITY_INVALID"
        }

        $length = if ($friendlyName.EndsWith("Sha1", [StringComparison]::Ordinal)) { 40 } else { 64 }
        if ([string]$allow.Hash -notmatch "^[0-9A-Fa-f]{$length}$") {
            throw "POLICY_HASH_VALUE_INVALID"
        }
    }

    if ($expectedFriendlyNames.Count -ne 0) {
        throw "POLICY_HASH_RULE_MISSING"
    }

    $driverScenario = $xml.SelectSingleNode("//c:SigningScenario[@Value='131']", $namespace)
    $userScenario = $xml.SelectSingleNode("//c:SigningScenario[@Value='12']", $namespace)
    if ($null -eq $driverScenario -or $null -eq $userScenario -or
        @($driverScenario.SelectNodes(".//c:FileRuleRef", $namespace)).Count -ne 0) {
        throw "POLICY_SIGNING_SCENARIO_INVALID"
    }

    $userRefs = @($userScenario.SelectNodes(".//c:FileRuleRef", $namespace) | ForEach-Object { [string]$_.RuleID })
    if ($userRefs.Count -ne $ruleIds.Count) {
        throw "POLICY_USER_RULE_REFERENCE_INVALID"
    }

    foreach ($ruleId in $userRefs) {
        if (-not $ruleIds.Contains($ruleId)) {
            throw "POLICY_USER_RULE_REFERENCE_INVALID"
        }
    }

    return [pscustomobject]@{
        PolicyId = $policyId
        BasePolicyId = $basePolicyId
        FriendlyName = [string]$RolloutMetadata.policy.friendlyName
        Version = [string]$xml.SiPolicy.VersionEx
        XmlPath = $xmlPath
        CipPath = $cipPath
        HashRuleCount = $allows.Count
    }
}

function Get-CiToolPath {
    $path = Join-Path $env:windir "System32\CiTool.exe"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "CITOOL_NOT_FOUND"
    }

    return $path
}

function Invoke-CiToolJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $ciTool = Get-CiToolPath
    $output = @(& $ciTool @Arguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0) {
        throw "CITOOL_COMMAND_FAILED_$exitCode"
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    $start = $text.IndexOf('{')
    $end = $text.LastIndexOf('}')
    if ($start -lt 0 -or $end -lt $start) {
        throw "CITOOL_JSON_INVALID"
    }

    try {
        $result = $text.Substring($start, $end - $start + 1) | ConvertFrom-Json
    }
    catch {
        throw "CITOOL_JSON_INVALID"
    }

    if ($null -ne $result.OperationResult -and [int64]$result.OperationResult -ne 0) {
        throw "CITOOL_OPERATION_FAILED"
    }

    return $result
}

function Get-CiPolicyInventory {
    $result = Invoke-CiToolJson -Arguments @("--list-policies", "-json")
    if ($null -eq $result -or $null -eq $result.Policies) {
        throw "CITOOL_POLICY_LIST_INVALID"
    }

    return @($result.Policies)
}

function Get-PolicyById {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Policies,
        [Parameter(Mandatory = $true)][string]$PolicyId
    )

    $normalized = ConvertTo-NormalizedGuid $PolicyId
    return @($Policies | Where-Object { (ConvertTo-NormalizedGuid $_.PolicyID) -eq $normalized })
}

function Get-CiBooleanProperty {
    param(
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return [pscustomobject]@{ Valid = $false; Value = $false }
    }

    try {
        return [pscustomobject]@{ Valid = $true; Value = (ConvertTo-StrictBoolean $property.Value) }
    }
    catch {
        return [pscustomobject]@{ Valid = $false; Value = $false }
    }
}

function Get-PersonalProjectPolicyState {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Policies,
        [Parameter(Mandatory = $true)][object]$PolicyMetadata
    )

    $matches = @(Get-PolicyById -Policies $Policies -PolicyId $PolicyMetadata.PolicyId)
    $identityValid = $false
    $onDisk = $false
    $authorized = $false
    $enforced = $false

    if ($matches.Count -eq 1) {
        $policy = $matches[0]
        $basePolicyId = $null
        $basePolicyProperty = $policy.PSObject.Properties["BasePolicyID"]
        if ($null -ne $basePolicyProperty) {
            try {
                $basePolicyId = ConvertTo-NormalizedGuid $basePolicyProperty.Value
            }
            catch {
                $basePolicyId = $null
            }
        }

        $friendlyNameProperty = $policy.PSObject.Properties["FriendlyName"]
        $versionProperty = $policy.PSObject.Properties["VersionString"]
        $optionsProperty = $policy.PSObject.Properties["PolicyOptions"]
        $options = @(
            if ($null -ne $optionsProperty) {
                $optionsProperty.Value | ForEach-Object { [string]$_ }
            }
        )

        $systemPolicy = Get-CiBooleanProperty -InputObject $policy -Name "IsSystemPolicy"
        $signedPolicy = Get-CiBooleanProperty -InputObject $policy -Name "IsSignedPolicy"
        $onDiskState = Get-CiBooleanProperty -InputObject $policy -Name "IsOnDisk"
        $authorizedState = Get-CiBooleanProperty -InputObject $policy -Name "IsAuthorized"
        $enforcedState = Get-CiBooleanProperty -InputObject $policy -Name "IsEnforced"

        $identityValid = $basePolicyId -eq (ConvertTo-NormalizedGuid $PolicyMetadata.BasePolicyId) -and
            $null -ne $friendlyNameProperty -and
            [string]::Equals([string]$friendlyNameProperty.Value, [string]$PolicyMetadata.FriendlyName, [StringComparison]::Ordinal) -and
            $null -ne $versionProperty -and
            [string]::Equals([string]$versionProperty.Value, [string]$PolicyMetadata.Version, [StringComparison]::Ordinal) -and
            $systemPolicy.Valid -and -not $systemPolicy.Value -and
            $signedPolicy.Valid -and -not $signedPolicy.Value -and
            $null -ne $optionsProperty -and
            $options.Count -eq 1 -and
            [string]::Equals($options[0], "Enabled:Unsigned System Integrity Policy", [StringComparison]::Ordinal)
        $onDisk = $onDiskState.Valid -and $onDiskState.Value
        $authorized = $authorizedState.Valid -and $authorizedState.Value
        $enforced = $enforcedState.Valid -and $enforcedState.Value
    }

    return [pscustomobject]@{
        Count = $matches.Count
        Present = ($matches.Count -eq 1)
        IdentityValid = $identityValid
        OnDisk = $onDisk
        Authorized = $authorized
        Enforced = $enforced
        Eligible = ($matches.Count -eq 1 -and $identityValid -and $onDisk -and $authorized -and $enforced)
    }
}

function Get-PersonalRolloutDecision {
    param(
        [Parameter(Mandatory = $true)][int]$Build,
        [AllowNull()][object]$SmartAppControlState,
        [Parameter(Mandatory = $true)][int]$SacBaseCount,
        [Parameter(Mandatory = $true)][int]$ExtraBasePolicyCount,
        [Parameter(Mandatory = $true)][object]$ProjectPolicyState
    )

    $reasons = New-Object 'System.Collections.Generic.List[string]'
    if ($Build -lt 22621) {
        $reasons.Add("WINDOWS_11_22H2_OR_NEWER_REQUIRED")
    }
    if ($ExtraBasePolicyCount -ne 0) {
        $reasons.Add("ADDITIONAL_ENFORCED_BASE_POLICY_DETECTED")
    }
    if ($ProjectPolicyState.Count -gt 1) {
        $reasons.Add("PROJECT_POLICY_INVENTORY_AMBIGUOUS")
    }

    $mode = "BLOCKED"
    if ($SmartAppControlState -eq 1) {
        if ($SacBaseCount -ne 1) {
            $reasons.Add("SAC_ENFORCEMENT_BASE_NOT_ACTIVE")
        }

        if ($ProjectPolicyState.Count -eq 0) {
            $reasons.Add("SAC_UNSIGNED_SUPPLEMENTAL_NOT_AUTHORIZED")
        }
        elseif ($ProjectPolicyState.Count -eq 1) {
            if (-not $ProjectPolicyState.IdentityValid) {
                $reasons.Add("PROJECT_POLICY_IDENTITY_MISMATCH")
            }
            if (-not $ProjectPolicyState.OnDisk) {
                $reasons.Add("PROJECT_POLICY_NOT_ON_DISK")
            }
            if (-not $ProjectPolicyState.Authorized) {
                $reasons.Add("PROJECT_POLICY_NOT_AUTHORIZED")
            }
            if (-not $ProjectPolicyState.Enforced) {
                $reasons.Add("PROJECT_POLICY_NOT_ENFORCED")
            }
        }

        if ($reasons.Count -eq 0) {
            $mode = "SAC_ENFORCED_READY"
        }
    }
    elseif ($SmartAppControlState -eq 0) {
        if ($SacBaseCount -ne 0) {
            $reasons.Add("SAC_STATE_POLICY_MISMATCH")
        }
        if ($ProjectPolicyState.Count -ne 0) {
            $reasons.Add("PROJECT_POLICY_UNEXPECTED_WHILE_SAC_OFF")
        }
        if ($reasons.Count -eq 0) {
            $mode = "SAC_OFF_DIRECT_TEST"
        }
    }
    elseif ($SmartAppControlState -eq 2) {
        $reasons.Add("SAC_EVALUATION_NOT_SUPPORTED_FOR_PILOT")
    }
    else {
        $reasons.Add("SAC_STATE_UNKNOWN")
    }

    return [pscustomobject]@{
        Mode = $mode
        Reasons = @($reasons)
    }
}

function Get-PersonalRolloutReadiness {
    param(
        [Parameter(Mandatory = $true)][object[]]$Policies,
        [Parameter(Mandatory = $true)][object]$PolicyMetadata
    )

    $operatingSystem = Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion"
    $build = [int]$operatingSystem.CurrentBuildNumber
    $sacValue = $null
    try {
        $sacValue = [int](Get-ItemPropertyValue -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy" -Name "VerifiedAndReputablePolicyState")
    }
    catch {
        $sacValue = $null
    }

    $enforced = @($Policies | Where-Object {
        $state = Get-CiBooleanProperty -InputObject $_ -Name "IsEnforced"
        $state.Valid -and $state.Value
    })
    $sacBase = @(Get-PolicyById -Policies $enforced -PolicyId $script:SacEnforcementBasePolicyId | Where-Object {
        (ConvertTo-NormalizedGuid $_.BasePolicyID) -eq $script:SacEnforcementBasePolicyId -and
        [string]::Equals([string]$_.FriendlyName, $script:SacEnforcementFriendlyName, [StringComparison]::Ordinal)
    })

    $extraBasePolicies = @($enforced | Where-Object {
        $id = ConvertTo-NormalizedGuid $_.PolicyID
        $base = ConvertTo-NormalizedGuid $_.BasePolicyID
        $isSystem = if ($null -ne $_.PSObject.Properties["IsSystemPolicy"]) { ConvertTo-StrictBoolean $_.IsSystemPolicy } else { $false }
        $id -eq $base -and $id -ne $script:SacEnforcementBasePolicyId -and -not $isSystem
    })
    $projectPolicyState = Get-PersonalProjectPolicyState -Policies $Policies -PolicyMetadata $PolicyMetadata
    $decision = Get-PersonalRolloutDecision -Build $build -SmartAppControlState $sacValue -SacBaseCount $sacBase.Count -ExtraBasePolicyCount $extraBasePolicies.Count -ProjectPolicyState $projectPolicyState

    return [pscustomobject]@{
        Mode = $decision.Mode
        Reasons = @($decision.Reasons)
        IsAdministrator = Test-IsAdministrator
        ProductName = [string]$operatingSystem.ProductName
        EditionId = [string]$operatingSystem.EditionID
        DisplayVersion = [string]$operatingSystem.DisplayVersion
        Build = $build
        SmartAppControlState = $sacValue
        SacBaseActive = ($sacBase.Count -eq 1)
        ProjectPolicyPresent = $projectPolicyState.Present
        ProjectPolicyIdentityValid = $projectPolicyState.IdentityValid
        ProjectPolicyOnDisk = $projectPolicyState.OnDisk
        ProjectPolicyAuthorized = $projectPolicyState.Authorized
        ProjectPolicyEnforced = $projectPolicyState.Enforced
        ExtraBasePolicyCount = $extraBasePolicies.Count
    }
}

function Wait-ForPolicyState {
    param(
        [Parameter(Mandatory = $true)][string]$PolicyId,
        [Parameter(Mandatory = $true)][bool]$Present,
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $policies = Get-CiPolicyInventory
        $matches = @(Get-PolicyById -Policies $policies -PolicyId $PolicyId)
        $isPresent = $matches.Count -ne 0
        if ($isPresent -eq $Present) {
            return [pscustomobject]@{ Matched = $true; Policies = $policies; Policy = if ($matches.Count -eq 1) { $matches[0] } else { $null } }
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    return [pscustomobject]@{ Matched = $false; Policies = $policies; Policy = if ($matches.Count -eq 1) { $matches[0] } else { $null } }
}
