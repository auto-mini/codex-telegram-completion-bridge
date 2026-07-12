using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class AuthenticodeTrustVerifierTests
{
    [Fact]
    public void Accepts_a_trusted_microsoft_windows_binary_without_network_access()
    {
        var dotnetHost = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dotnet",
            "dotnet.exe");

        AuthenticodeTrustVerifier.EnsureTrusted(dotnetHost, allowNetwork: false);
    }

    [Fact]
    public void Rejects_an_unsigned_file()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "unsigned.exe");
        File.WriteAllText(path, "not a signed executable");

        Assert.Throws<InvalidDataException>(() =>
            AuthenticodeTrustVerifier.EnsureTrusted(path, allowNetwork: false));
    }

    [Fact]
    public void Rejects_a_timestamp_certificate_that_does_not_match_the_release_record()
    {
        var dotnetHost = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dotnet",
            "dotnet.exe");

        Assert.Throws<InvalidDataException>(() =>
            AuthenticodeTrustVerifier.EnsureTrusted(
                dotnetHost,
                allowNetwork: false,
                expectedTimestampCertificateThumbprint: new string('0', 40)));
    }
}
