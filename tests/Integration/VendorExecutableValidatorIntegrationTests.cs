using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class VendorExecutableValidatorIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VendorValidatorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Accepts_exact_contained_vendor_and_detects_change()
    {
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, BridgeConstants.VendorExecutableName);
        File.WriteAllBytes(executable, [1, 2, 3]);
        var validator = new VendorExecutableValidator(root);

        var result = validator.ValidateArgv([executable, BridgeConstants.VendorArgument], new string('a', 64), DateTimeOffset.UtcNow);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Record);
        File.WriteAllBytes(executable, [1, 2, 3, 4]);
        Assert.Equal("VENDOR_FILE_CHANGED", validator.ValidateCaptured(result.Record!).OperationCode);
    }

    [Fact]
    public void Rejects_outside_path_extra_argument_and_wrong_name()
    {
        Directory.CreateDirectory(root);
        var validator = new VendorExecutableValidator(root);
        var outside = Path.Combine(Path.GetTempPath(), BridgeConstants.VendorExecutableName);
        File.WriteAllBytes(outside, [1]);
        var wrongName = Path.Combine(root, "other.exe");
        File.WriteAllBytes(wrongName, [1]);

        Assert.Equal("VENDOR_PATH_ESCAPE", validator.ValidateArgv([outside, "turn-ended"], "hash", DateTimeOffset.UtcNow).OperationCode);
        Assert.Equal("VENDOR_ARGV_UNSUPPORTED", validator.ValidateArgv([outside, "turn-ended", "extra"], "hash", DateTimeOffset.UtcNow).OperationCode);
        Assert.Equal("VENDOR_PATH_UNSUPPORTED", validator.ValidateArgv([wrongName, "turn-ended"], "hash", DateTimeOffset.UtcNow).OperationCode);

        File.Delete(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
