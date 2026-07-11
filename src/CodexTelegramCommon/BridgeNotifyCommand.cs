using System.Text.Json;

namespace CodexTelegramCommon;

public enum BridgeNotifyShape
{
    None,
    Direct,
    VendorWrapped,
}

public sealed record BridgeNotifyMatch(
    BridgeNotifyShape Shape,
    IReadOnlyList<string>? OuterVendorArgv = null)
{
    public bool IsActive => Shape is BridgeNotifyShape.Direct or BridgeNotifyShape.VendorWrapped;
}

public static class BridgeNotifyCommand
{
    public static BridgeNotifyMatch Match(IReadOnlyList<string>? argv, string expectedBridgePath)
    {
        if (argv is null)
        {
            return new BridgeNotifyMatch(BridgeNotifyShape.None);
        }

        if (IsExactBridgeArgv(argv, expectedBridgePath))
        {
            return new BridgeNotifyMatch(BridgeNotifyShape.Direct);
        }

        if (TryReadVendorWrappedBridge(argv, expectedBridgePath, out var outerVendorArgv))
        {
            return new BridgeNotifyMatch(BridgeNotifyShape.VendorWrapped, outerVendorArgv);
        }

        return new BridgeNotifyMatch(BridgeNotifyShape.None);
    }

    public static bool IsExactBridgeArgv(IReadOnlyList<string> argv, string expectedBridgePath)
    {
        if (argv.Count != 2 ||
            !string.Equals(argv[1], "hook", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(argv[0]) ||
            !Path.IsPathFullyQualified(expectedBridgePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(argv[0]),
                Path.GetFullPath(expectedBridgePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool ReferencesBridge(IReadOnlyList<string>? argv, string expectedBridgePath)
    {
        if (argv is null)
        {
            return false;
        }

        if (argv.Any(value => IsBridgePath(value, expectedBridgePath)))
        {
            return true;
        }

        foreach (var value in argv)
        {
            if (value.Contains("CodexTelegramBridge.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Length > BridgeConstants.MaxPreviousNotifyUtf16Length)
            {
                continue;
            }

            try
            {
                var nested = JsonSerializer.Deserialize<string[]>(value, JsonDefaults.Options);
                if (nested?.Any(item => IsBridgePath(item, expectedBridgePath)) == true)
                {
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static bool TryReadVendorWrappedBridge(
        IReadOnlyList<string> argv,
        string expectedBridgePath,
        out IReadOnlyList<string>? outerVendorArgv)
    {
        outerVendorArgv = null;
        if (argv.Count != 4 ||
            !string.Equals(argv[1], BridgeConstants.VendorArgument, StringComparison.Ordinal) ||
            !string.Equals(argv[2], BridgeConstants.VendorPreviousNotifyArgument, StringComparison.Ordinal) ||
            argv[3].Length > BridgeConstants.MaxPreviousNotifyUtf16Length)
        {
            return false;
        }

        string[]? nested;
        try
        {
            nested = JsonSerializer.Deserialize<string[]>(argv[3], JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (nested is null || !IsExactBridgeArgv(nested, expectedBridgePath))
        {
            return false;
        }

        outerVendorArgv = [argv[0], argv[1]];
        return true;
    }

    private static bool IsBridgePath(string value, string expectedBridgePath)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(value), Path.GetFullPath(expectedBridgePath), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetFileName(value), "CodexTelegramBridge.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
