using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class PackageManifestTests
{
    [Fact]
    public void Verifies_required_binaries_and_outer_hash()
    {
        using var temp = new TempDirectory();
        var bin = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "CodexTelegramBridge.exe"), "bridge");
        File.WriteAllText(Path.Combine(bin, "CodexTelegramCtl.exe"), "ctl");
        var manifestText = string.Join("\n",
            $"{Hashing.Sha256File(Path.Combine(bin, "CodexTelegramBridge.exe"))}  bin/CodexTelegramBridge.exe",
            $"{Hashing.Sha256File(Path.Combine(bin, "CodexTelegramCtl.exe"))}  bin/CodexTelegramCtl.exe") + "\n";
        File.WriteAllText(Path.Combine(temp.Path, "manifest.sha256"), manifestText, new UTF8Encoding(false));

        var manifest = PackageManifest.LoadAndVerify(temp.Path);

        Assert.Equal(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(manifestText)), manifest.ManifestSha256);
        Assert.Equal(2, manifest.Entries.Count);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("bin//bad")]
    [InlineData("bin/./bad")]
    public void Rejects_unsafe_manifest_paths(string path)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "manifest.sha256"), $"{new string('a', 64)}  {path}\n");

        Assert.Throws<InvalidDataException>(() => PackageManifest.LoadAndVerify(temp.Path));
    }

    [Fact]
    public void Rejects_unlisted_shipped_artifact()
    {
        using var temp = new TempDirectory();
        var bin = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "CodexTelegramBridge.exe"), "bridge");
        File.WriteAllText(Path.Combine(bin, "CodexTelegramCtl.exe"), "ctl");
        File.WriteAllText(Path.Combine(temp.Path, "unlisted-secret.txt"), "must not ship");
        File.WriteAllLines(Path.Combine(temp.Path, "manifest.sha256"),
        [
            $"{Hashing.Sha256File(Path.Combine(bin, "CodexTelegramBridge.exe"))}  bin/CodexTelegramBridge.exe",
            $"{Hashing.Sha256File(Path.Combine(bin, "CodexTelegramCtl.exe"))}  bin/CodexTelegramCtl.exe",
        ], new UTF8Encoding(false));

        Assert.Throws<InvalidDataException>(() => PackageManifest.LoadAndVerify(temp.Path));
    }

}
