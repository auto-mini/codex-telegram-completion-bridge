using System.Text.Json;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class BridgeNotifyCommandTests
{
    private readonly string bridge = Path.GetFullPath(Path.Combine("C:\\", "bridge", "CodexTelegramBridge.exe"));
    private readonly string vendor = Path.GetFullPath(Path.Combine("C:\\", "vendor", BridgeConstants.VendorExecutableName));

    [Fact]
    public void Matches_direct_bridge()
    {
        var match = BridgeNotifyCommand.Match([bridge, "hook"], bridge);

        Assert.Equal(BridgeNotifyShape.Direct, match.Shape);
        Assert.Null(match.OuterVendorArgv);
    }

    [Fact]
    public void Matches_exact_vendor_wrapper_and_extracts_outer_handler()
    {
        var previous = JsonSerializer.Serialize(new[] { bridge, "hook" });

        var match = BridgeNotifyCommand.Match(
            [vendor, BridgeConstants.VendorArgument, BridgeConstants.VendorPreviousNotifyArgument, previous],
            bridge);

        Assert.Equal(BridgeNotifyShape.VendorWrapped, match.Shape);
        Assert.Equal([vendor, BridgeConstants.VendorArgument], match.OuterVendorArgv);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("[\"C:\\\\other\\\\CodexTelegramBridge.exe\",\"hook\"]")]
    [InlineData("[\"C:\\\\bridge\\\\CodexTelegramBridge.exe\",\"hook\",\"extra\"]")]
    public void Rejects_malformed_or_non_exact_previous_notify(string previous)
    {
        var match = BridgeNotifyCommand.Match(
            [vendor, BridgeConstants.VendorArgument, BridgeConstants.VendorPreviousNotifyArgument, previous],
            bridge);

        Assert.Equal(BridgeNotifyShape.None, match.Shape);
    }

    [Fact]
    public void Detects_nested_bridge_reference_even_when_wrapper_is_unsupported()
    {
        var previous = JsonSerializer.Serialize(new[] { bridge, "hook", "unexpected" });
        var argv = new[] { vendor, BridgeConstants.VendorArgument, "--unsupported", previous };

        Assert.True(BridgeNotifyCommand.ReferencesBridge(argv, bridge));
        Assert.Equal(BridgeNotifyShape.None, BridgeNotifyCommand.Match(argv, bridge).Shape);
    }

    [Fact]
    public void Detects_bridge_reference_in_oversized_unparseable_argument()
    {
        var oversized = new string('x', BridgeConstants.MaxPreviousNotifyUtf16Length + 1) + "CodexTelegramBridge.exe";

        Assert.True(BridgeNotifyCommand.ReferencesBridge(["custom.exe", oversized], bridge));
    }
}
