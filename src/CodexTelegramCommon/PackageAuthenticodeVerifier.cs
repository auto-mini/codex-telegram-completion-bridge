using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace CodexTelegramCommon;

public sealed record AuthenticodeFileRecord(
    string Path,
    string UnsignedSha256,
    string SignedSha256,
    string TimestampCertificateThumbprint);

public sealed record AuthenticodeSigningRecord(
    int SchemaVersion,
    DateTimeOffset SignedAtUtc,
    string SignerSubject,
    string SignerThumbprint,
    DateTimeOffset SignerNotBeforeUtc,
    DateTimeOffset SignerNotAfterUtc,
    string SigningCaSha256,
    string TrustedRootSha256,
    string TimestampServer,
    IReadOnlyList<AuthenticodeFileRecord> Files);

public static class PackageAuthenticodeVerifier
{
    public const string SigningRecordPath = "signing-record.json";
    private const int MaximumSigningRecordBytes = 64 * 1024;
    private static readonly IReadOnlySet<string> RequiredSignedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bin/CodexTelegramBridge.exe",
        "bin/CodexTelegramCtl.exe",
        "bin/e_sqlite3.dll",
        "scripts/install.ps1",
        "scripts/uninstall.ps1",
    };
    private static readonly IReadOnlySet<string> AllowedCertumSigningCaSha256 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "5AC82CBE9F28351E85D5262293BCFC8BACABEAD1294F248C1DF17F81CE5AC3CE",
    };
    private static readonly IReadOnlySet<string> AllowedCertumRootSha256 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "D8E0FEBC1DB2E38D00940F37D27D41344D993E734B99D5656D9778D4D8143624",
        "5C58468D55F58E497E743982D2B50010B6D165374ACF83A7D4A32DB768C4408E",
        "B676F2EDDAE8775CD36CB0F63CD1D4603961F49E6265BA013A2F0307B6D0B804",
        "FE7696573855773E37A95E7AD4D9CC96C30157C15D31765BA9B15704E1AE78FD",
    };

    public static AuthenticodeSigningRecord? VerifyIfPresent(PackageManifest package, bool allowNetwork)
    {
        var manifestEntry = package.Entries.SingleOrDefault(entry =>
            string.Equals(entry.RelativePath, SigningRecordPath, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            return null;
        }

        var recordPath = package.ResolveContainedPath(manifestEntry.RelativePath);
        var bytes = File.ReadAllBytes(recordPath);
        if (bytes.Length is 0 or > MaximumSigningRecordBytes)
        {
            throw new InvalidDataException("The Authenticode signing record has an invalid size.");
        }

        AuthenticodeSigningRecord record;
        try
        {
            var json = new UTF8Encoding(false, true).GetString(bytes);
            record = JsonSerializer.Deserialize<AuthenticodeSigningRecord>(json, JsonDefaults.Options)
                     ?? throw new InvalidDataException("The Authenticode signing record is empty.");
        }
        catch (Exception exception) when (exception is DecoderFallbackException or JsonException)
        {
            throw new InvalidDataException("The Authenticode signing record is invalid.", exception);
        }

        ValidateRecord(record, package);
        foreach (var file in record.Files)
        {
            var path = package.ResolveContainedPath(file.Path);
            AuthenticodeTrustVerifier.EnsureTrusted(path, allowNetwork, file.TimestampCertificateThumbprint);
            EnsureExpectedSigner(path, record, allowNetwork);
        }
        return record;
    }

    private static void ValidateRecord(AuthenticodeSigningRecord record, PackageManifest package)
    {
        if (record.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(record.SignerSubject) ||
            record.SignerSubject.Length > 1024 ||
            !IsThumbprint(record.SignerThumbprint) ||
            !IsSha256(record.SigningCaSha256) ||
            !AllowedCertumSigningCaSha256.Contains(record.SigningCaSha256) ||
            !IsSha256(record.TrustedRootSha256) ||
            !AllowedCertumRootSha256.Contains(record.TrustedRootSha256) ||
            record.SignerNotAfterUtc <= record.SignerNotBeforeUtc ||
            record.SignedAtUtc < record.SignerNotBeforeUtc - TimeSpan.FromMinutes(5) ||
            record.SignedAtUtc > record.SignerNotAfterUtc + TimeSpan.FromMinutes(5) ||
            !IsSafeTimestampServer(record.TimestampServer) ||
            record.Files is null ||
            record.Files.Count != RequiredSignedPaths.Count)
        {
            throw new InvalidDataException("The Authenticode signing record failed validation.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in record.Files)
        {
            if (file is null ||
                !RequiredSignedPaths.Contains(file.Path) ||
                !seen.Add(file.Path) ||
                !IsSha256(file.UnsignedSha256) ||
                !IsSha256(file.SignedSha256) ||
                string.Equals(file.UnsignedSha256, file.SignedSha256, StringComparison.OrdinalIgnoreCase) ||
                !IsThumbprint(file.TimestampCertificateThumbprint))
            {
                throw new InvalidDataException("The Authenticode signing record contains an invalid file entry.");
            }

            var manifestEntry = package.Entries.SingleOrDefault(entry =>
                string.Equals(entry.RelativePath, file.Path, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null ||
                !string.Equals(manifestEntry.Sha256, file.SignedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Authenticode signing record does not match the package manifest.");
            }
        }
    }

    private static void EnsureExpectedSigner(
        string path,
        AuthenticodeSigningRecord record,
        bool allowNetwork)
    {
        X509Certificate legacyCertificate;
        try
        {
#pragma warning disable SYSLIB0057 // .NET 8 has no X509CertificateLoader replacement for signed files.
            legacyCertificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The Authenticode signer certificate could not be read.", exception);
        }
        using (legacyCertificate)
        using (var certificate = new X509Certificate2(legacyCertificate))
        {
            if (!string.Equals(certificate.Thumbprint, record.SignerThumbprint, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(certificate.Subject, record.SignerSubject, StringComparison.Ordinal) ||
                Math.Abs((certificate.NotBefore.ToUniversalTime() - record.SignerNotBeforeUtc.UtcDateTime).TotalSeconds) > 1 ||
                Math.Abs((certificate.NotAfter.ToUniversalTime() - record.SignerNotAfterUtc.UtcDateTime).TotalSeconds) > 1)
            {
                throw new InvalidDataException("An Authenticode signer does not match the release signing record.");
            }

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null || rsa.KeySize < 3072)
            {
                throw new InvalidDataException("The release is not signed with the required RSA key strength.");
            }

            var codeSigningEku = certificate.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
                .Any(oid => string.Equals(oid.Value, "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal));
            if (!codeSigningEku)
            {
                throw new InvalidDataException("The release signer is not a code-signing certificate.");
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.VerificationTime = record.SignedAtUtc.UtcDateTime;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(20);
            chain.ChainPolicy.DisableCertificateDownloads = !allowNetwork;
            if (!chain.Build(certificate) || chain.ChainElements.Count < 3)
            {
                throw new InvalidDataException("The Authenticode signer chain could not be reconstructed.");
            }

            var rootSha256 = CertificateSha256(chain.ChainElements[^1].Certificate);
            var signingCaSha256 = chain.ChainElements
                .Cast<X509ChainElement>()
                .Skip(1)
                .SkipLast(1)
                .Select(element => CertificateSha256(element.Certificate))
                .SingleOrDefault(AllowedCertumSigningCaSha256.Contains);
            if (!AllowedCertumRootSha256.Contains(rootSha256) ||
                !string.Equals(signingCaSha256, record.SigningCaSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Authenticode signer chain is not the pinned Certum public code-signing chain.");
            }
        }
    }

    private static string CertificateSha256(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static bool IsSafeTimestampServer(string? value) =>
        value is { Length: <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsThumbprint(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);
}

internal static class AuthenticodeTrustVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChainExcludeRoot = 0x00000080;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
    private const uint WtdDisableMd2Md4 = 0x00002000;

    public static void EnsureTrusted(
        string path,
        bool allowNetwork,
        string? expectedTimestampCertificateThumbprint = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Authenticode verification requires Windows.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Authenticode target is missing.");
        }

        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = Path.GetFullPath(path),
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var marshaled = false;
        var trustData = new WinTrustData();
        var action = GenericVerifyV2;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            marshaled = true;
            trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeWholeChain,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionVerify,
                ProviderFlags = WtdRevocationCheckChainExcludeRoot |
                                WtdDisableMd2Md4 |
                                (allowNetwork ? 0u : WtdCacheOnlyUrlRetrieval),
            };
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
            if (result != 0)
            {
                throw new InvalidDataException($"Authenticode trust verification failed with 0x{unchecked((uint)result):X8}.");
            }

            var actualTimestampThumbprints = ReadTimestampCertificateThumbprints(trustData.StateData);
            if (actualTimestampThumbprints.Count == 0 ||
                expectedTimestampCertificateThumbprint is not null &&
                !actualTimestampThumbprints.Contains(expectedTimestampCertificateThumbprint, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Authenticode signature has no matching trusted timestamp countersignature.");
            }
        }
        finally
        {
            if (trustData.StateData != IntPtr.Zero)
            {
                trustData.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
            }
            if (marshaled)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            }
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private static IReadOnlyList<string> ReadTimestampCertificateThumbprints(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero)
        {
            return [];
        }

        var providerData = WTHelperProvDataFromStateData(stateData);
        if (providerData == IntPtr.Zero)
        {
            return [];
        }
        var primaryPointer = WTHelperGetProvSignerFromChain(providerData, 0, false, 0);
        if (primaryPointer == IntPtr.Zero)
        {
            return [];
        }
        var primary = Marshal.PtrToStructure<CryptProviderSigner>(primaryPointer);
        var thumbprints = new List<string>();
        for (uint index = 0; index < primary.CounterSignerCount; index++)
        {
            var timestampSignerPointer = WTHelperGetProvSignerFromChain(providerData, 0, true, index);
            if (timestampSignerPointer == IntPtr.Zero)
            {
                continue;
            }
            var timestampSigner = Marshal.PtrToStructure<CryptProviderSigner>(timestampSignerPointer);
            if ((timestampSigner.SignerType & 0x00000010) == 0 || timestampSigner.Error != 0)
            {
                continue;
            }
            var providerCertificatePointer = WTHelperGetProvCertFromChain(timestampSignerPointer, 0);
            if (providerCertificatePointer == IntPtr.Zero)
            {
                continue;
            }
            var providerCertificate = Marshal.PtrToStructure<CryptProviderCertificate>(providerCertificatePointer);
            if (providerCertificate.CertificateContext == IntPtr.Zero)
            {
                continue;
            }
#pragma warning disable SYSLIB0057 // .NET 8 has no X509CertificateLoader replacement for CERT_CONTEXT handles.
            using var certificate = new X509Certificate2(providerCertificate.CertificateContext);
#pragma warning restore SYSLIB0057
            thumbprints.Add(certificate.Thumbprint);
        }
        return thumbprints;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [In] ref Guid actionId,
        [In] ref WinTrustData trustData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner,
        uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderSigner
    {
        public uint StructSize;
        public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
        public uint CertificateChainCount;
        public IntPtr CertificateChain;
        public uint SignerType;
        public IntPtr SignerInfo;
        public uint Error;
        public uint CounterSignerCount;
        public IntPtr CounterSigners;
        public IntPtr ChainContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCertificate
    {
        public uint StructSize;
        public IntPtr CertificateContext;
        public int Commercial;
        public int TrustedRoot;
        public int SelfSigned;
        public int TestCertificate;
        public uint RevokedReason;
        public uint Confidence;
        public uint Error;
        public IntPtr TrustListContext;
        public int TrustListSignerCertificate;
        public IntPtr CtlContext;
        public uint CtlError;
        public int IsCyclic;
        public IntPtr ChainElement;
    }
}
