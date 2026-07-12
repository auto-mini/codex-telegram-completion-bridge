Set-StrictMode -Version Latest

$script:AllowedCertumSigningCaSha256 = @(
    '5AC82CBE9F28351E85D5262293BCFC8BACABEAD1294F248C1DF17F81CE5AC3CE'
)
$script:AllowedCertumRootSha256 = @(
    'D8E0FEBC1DB2E38D00940F37D27D41344D993E734B99D5656D9778D4D8143624',
    '5C58468D55F58E497E743982D2B50010B6D165374ACF83A7D4A32DB768C4408E',
    'B676F2EDDAE8775CD36CB0F63CD1D4603961F49E6265BA013A2F0307B6D0B804',
    'FE7696573855773E37A95E7AD4D9CC96C30157C15D31765BA9B15704E1AE78FD'
)

function Get-CertificateSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha256.ComputeHash($Certificate.RawData)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Normalize-CertificateThumbprint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Thumbprint)

    $normalized = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    if ($normalized -notmatch '^[0-9A-F]{40}$') {
        throw "The code-signing certificate thumbprint must contain exactly 40 hexadecimal characters."
    }
    return $normalized
}

function Resolve-TimestampServerUri {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TimestampServer)

    $uri = $null
    if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https') -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "The timestamp server must be an absolute HTTP(S) URI without credentials or a fragment."
    }
    return $uri.AbsoluteUri
}

function Resolve-CodeSigningCertificate {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Thumbprint)

    $normalized = Normalize-CertificateThumbprint -Thumbprint $Thumbprint
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$normalized" -ErrorAction SilentlyContinue
    if ($null -eq $certificate) {
        throw "The requested code-signing certificate is not available in Cert:\CurrentUser\My."
    }
    if (-not $certificate.HasPrivateKey) {
        throw "The requested certificate does not expose an accessible private key."
    }

    $now = [DateTime]::UtcNow
    if ($certificate.NotBefore.ToUniversalTime() -gt $now -or $certificate.NotAfter.ToUniversalTime() -le $now) {
        throw "The requested code-signing certificate is not currently valid."
    }

    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    $ekuExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        Select-Object -First 1
    if ($null -eq $ekuExtension -or
        -not ($ekuExtension.EnhancedKeyUsages | Where-Object { $_.Value -eq $codeSigningOid })) {
        throw "The requested certificate is not restricted to the Code Signing enhanced key usage."
    }

    $keyUsageExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.15' } |
        Select-Object -First 1
    if ($null -eq $keyUsageExtension -or
        -not (($keyUsageExtension.KeyUsages -band [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) -ne 0)) {
        throw "The requested certificate does not permit digital signatures."
    }

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    if ($null -eq $rsa) {
        throw "Smart App Control requires an RSA code-signing certificate; the requested certificate is not RSA."
    }
    try {
        if ($rsa.KeySize -lt 3072) {
            throw "The RSA code-signing key must be at least 3072 bits."
        }
    }
    finally {
        $rsa.Dispose()
    }

    $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $chain.ChainPolicy.RevocationFlag = [System.Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
        $chain.ChainPolicy.VerificationFlags = [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
        $chain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(20)
        $chain.ChainPolicy.VerificationTime = Get-Date
        if (-not $chain.Build($certificate)) {
            $statuses = @($chain.ChainStatus | ForEach-Object { $_.Status.ToString() }) -join ', '
            throw "The code-signing certificate does not build an online-revocation-checked trusted chain: $statuses"
        }
        $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
        $rootSha256 = Get-CertificateSha256 -Certificate $root
        if ($rootSha256 -notin $script:AllowedCertumRootSha256) {
            throw "The code-signing chain does not terminate at a pinned Certum RSA public root."
        }
        $signingCaSha256 = @($chain.ChainElements |
            Select-Object -Skip 1 |
            Select-Object -SkipLast 1 |
            ForEach-Object { Get-CertificateSha256 -Certificate $_.Certificate } |
            Where-Object { $_ -in $script:AllowedCertumSigningCaSha256 } |
            Select-Object -First 1)
        if ($signingCaSha256.Count -ne 1) {
            throw "The code-signing chain does not contain the pinned Certum Code Signing CA."
        }
    }
    finally {
        $chain.Dispose()
    }

    [pscustomobject]@{
        Certificate = $certificate
        Thumbprint = $normalized
        SigningCaSha256 = $signingCaSha256[0]
        RootSha256 = $rootSha256
    }
}

function Resolve-SignTool {
    [CmdletBinding()]
    param([string]$SignToolPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $candidates = @([System.IO.Path]::GetFullPath($SignToolPath))
    }
    else {
        $kitRoots = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
            (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) }
        $candidates = @($kitRoots | ForEach-Object {
            Get-ChildItem -LiteralPath $_ -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
                Select-Object -ExpandProperty FullName
        } | Sort-Object -Descending)

        $nugetRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
            $env:NUGET_PACKAGES
        }
        else {
            Join-Path $env:USERPROFILE '.nuget\packages'
        }
        $buildToolsRoot = Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools'
        if (Test-Path -LiteralPath $buildToolsRoot -PathType Container) {
            $candidates += @(Get-ChildItem -LiteralPath $buildToolsRoot -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
                Select-Object -ExpandProperty FullName |
                Sort-Object -Descending)
        }
    }

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf) -or
            [System.IO.Path]::GetFileName($candidate) -ine 'signtool.exe') {
            continue
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $candidate
        if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid -and
            $null -ne $signature.SignerCertificate -and
            ($signature.SignerCertificate.Subject -match '(^|,\s*)CN=Microsoft Corporation(,|$)' -or
             $signature.SignerCertificate.Subject -match '(^|,\s*)O=Microsoft Corporation(,|$)')) {
            return $candidate
        }
    }

    throw "A valid Microsoft SignTool was not found. Install the Windows SDK Signing Tools feature or pass -SignToolPath."
}

function Test-AuthenticodeSigningPrerequisites {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CertificateThumbprint,
        [Parameter(Mandatory)][string]$TimestampServer,
        [string]$SignToolPath
    )

    $timestampUri = Resolve-TimestampServerUri -TimestampServer $TimestampServer
    $certificateInfo = Resolve-CodeSigningCertificate -Thumbprint $CertificateThumbprint
    $resolvedSignTool = Resolve-SignTool -SignToolPath $SignToolPath
    [pscustomobject]@{
        CertificateInfo = $certificateInfo
        TimestampServer = $timestampUri
        SignToolPath = $resolvedSignTool
    }
}

function Invoke-SignToolChecked {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SignTool,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $SignTool @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Protect-ReleaseWithAuthenticode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$CertificateThumbprint,
        [Parameter(Mandatory)][string]$TimestampServer,
        [string]$SignToolPath
    )

    $staging = [System.IO.Path]::GetFullPath($StagingRoot).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $staging -PathType Container)) {
        throw "The release staging directory does not exist."
    }

    $prerequisites = Test-AuthenticodeSigningPrerequisites `
        -CertificateThumbprint $CertificateThumbprint `
        -TimestampServer $TimestampServer `
        -SignToolPath $SignToolPath
    $timestampUri = $prerequisites.TimestampServer
    $certificateInfo = $prerequisites.CertificateInfo
    $signTool = $prerequisites.SignToolPath
    $certificate = $certificateInfo.Certificate

    $targets = @(
        [pscustomobject]@{ RelativePath = 'bin/CodexTelegramBridge.exe'; Type = 'pe' },
        [pscustomobject]@{ RelativePath = 'bin/CodexTelegramCtl.exe'; Type = 'pe' },
        [pscustomobject]@{ RelativePath = 'bin/e_sqlite3.dll'; Type = 'pe' },
        [pscustomobject]@{ RelativePath = 'scripts/install.ps1'; Type = 'powershell' },
        [pscustomobject]@{ RelativePath = 'scripts/uninstall.ps1'; Type = 'powershell' }
    )

    $unsignedHashes = [ordered]@{}
    foreach ($target in $targets) {
        $path = Join-Path $staging ($target.RelativePath.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required signable release artifact is missing: $($target.RelativePath)"
        }
        $status = Get-AuthenticodeSignature -LiteralPath $path
        if ($status.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
            throw "Refusing to add a release signature to an artifact that is already signed: $($target.RelativePath)"
        }
        $unsignedHashes[$target.RelativePath] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    foreach ($target in $targets | Where-Object Type -eq 'pe') {
        $path = Join-Path $staging ($target.RelativePath.Replace('/', '\'))
        Invoke-SignToolChecked -SignTool $signTool -Arguments @(
            'sign', '/sha1', $certificateInfo.Thumbprint,
            '/fd', 'SHA256', '/tr', $timestampUri, '/td', 'SHA256', '/v', $path
        ) -FailureMessage "Authenticode signing failed for $($target.RelativePath)."
    }

    foreach ($target in $targets | Where-Object Type -eq 'powershell') {
        $path = Join-Path $staging ($target.RelativePath.Replace('/', '\'))
        $result = Set-AuthenticodeSignature -LiteralPath $path -Certificate $certificate `
            -HashAlgorithm SHA256 -TimestampServer $timestampUri
        if ($result.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Authenticode signing failed for $($target.RelativePath): $($result.Status)"
        }
    }

    $signedFiles = @()
    foreach ($target in $targets) {
        $path = Join-Path $staging ($target.RelativePath.Replace('/', '\'))
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $certificateInfo.Thumbprint -or
            $null -eq $signature.TimeStamperCertificate) {
            throw "Post-sign verification failed for $($target.RelativePath)."
        }
        if ($target.Type -eq 'pe') {
            Invoke-SignToolChecked -SignTool $signTool -Arguments @(
                'verify', '/pa', '/all', '/tw', '/v', $path
            ) -FailureMessage "SignTool verification failed for $($target.RelativePath)."
        }
        $signedFiles += [ordered]@{
            path = $target.RelativePath
            unsigned_sha256 = $unsignedHashes[$target.RelativePath]
            signed_sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            timestamp_certificate_thumbprint = $signature.TimeStamperCertificate.Thumbprint.ToLowerInvariant()
        }
    }

    $record = [ordered]@{
        schema_version = 1
        signed_at_utc = [DateTime]::UtcNow.ToString('O')
        signer_subject = $certificate.Subject
        signer_thumbprint = $certificateInfo.Thumbprint.ToLowerInvariant()
        signer_not_before_utc = $certificate.NotBefore.ToUniversalTime().ToString('O')
        signer_not_after_utc = $certificate.NotAfter.ToUniversalTime().ToString('O')
        signing_ca_sha256 = $certificateInfo.SigningCaSha256.ToLowerInvariant()
        trusted_root_sha256 = $certificateInfo.RootSha256.ToLowerInvariant()
        timestamp_server = $timestampUri
        files = $signedFiles
    }
    $recordPath = Join-Path $staging 'signing-record.json'
    $recordJson = $record | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($recordPath, $recordJson + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

    [pscustomobject]@{
        RecordPath = $recordPath
        SignerThumbprint = $certificateInfo.Thumbprint
        SigningCaSha256 = $certificateInfo.SigningCaSha256
        TrustedRootSha256 = $certificateInfo.RootSha256
    }
}
