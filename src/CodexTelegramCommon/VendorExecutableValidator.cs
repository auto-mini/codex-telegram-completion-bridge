using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexTelegramCommon;

public sealed class VendorExecutableValidator(string allowedRoot)
{
    public string AllowedRoot { get; } = Path.GetFullPath(allowedRoot);

    public static VendorExecutableValidator ForCurrentUser() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "runtimes",
            "cua_node"));

    public VendorValidationResult ValidateArgv(IReadOnlyList<string> argv, string capturedConfigSha256, DateTimeOffset capturedAtUtc)
    {
        if (argv.Count != 2 || !string.Equals(argv[1], BridgeConstants.VendorArgument, StringComparison.Ordinal))
        {
            return VendorValidationResult.Failure("VENDOR_ARGV_UNSUPPORTED");
        }

        var executable = argv[0];
        if (!Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetFileName(executable), BridgeConstants.VendorExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return VendorValidationResult.Failure("VENDOR_PATH_UNSUPPORTED");
        }

        try
        {
            var originalAttributes = File.GetAttributes(executable);
            if ((originalAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                return VendorValidationResult.Failure("VENDOR_FILE_UNSAFE");
            }

            var finalRoot = ResolveFinalPath(AllowedRoot, isDirectory: true);
            var finalExecutable = ResolveFinalPath(executable, isDirectory: false);
            var prefix = finalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!finalExecutable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return VendorValidationResult.Failure("VENDOR_PATH_ESCAPE");
            }

            var attributes = File.GetAttributes(finalExecutable);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) != 0)
            {
                return VendorValidationResult.Failure("VENDOR_FILE_UNSAFE");
            }

            var info = new FileInfo(finalExecutable);
            var record = new UpstreamRecord(
                BridgeConstants.SchemaVersion,
                [finalExecutable, BridgeConstants.VendorArgument],
                Hashing.Sha256File(finalExecutable),
                info.Length,
                capturedAtUtc,
                capturedConfigSha256,
                UpstreamKind.CodexComputerUseTurnEnded);
            return VendorValidationResult.Success(record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return VendorValidationResult.Failure("VENDOR_FILE_UNAVAILABLE");
        }
    }

    public VendorValidationResult ValidateCaptured(UpstreamRecord record)
    {
        try
        {
            record.ValidateShape();
        }
        catch (InvalidDataException)
        {
            return VendorValidationResult.Failure("UPSTREAM_RECORD_INVALID");
        }

        if (record.Kind == UpstreamKind.Absent)
        {
            return VendorValidationResult.Success(record);
        }

        var current = ValidateArgv(record.Argv, record.CapturedConfigSha256, record.CapturedAtUtc);
        if (!current.IsValid || current.Record is null)
        {
            return current;
        }

        return string.Equals(current.Record.ExecutableSha256, record.ExecutableSha256, StringComparison.Ordinal) &&
               current.Record.ExecutableSizeBytes == record.ExecutableSizeBytes
            ? VendorValidationResult.Success(record)
            : VendorValidationResult.Failure("VENDOR_FILE_CHANGED");
    }

    internal static string ResolveFinalPath(string path, bool isDirectory)
    {
        var flags = isDirectory ? NativeMethods.FileFlagBackupSemantics : 0u;
        using var handle = NativeMethods.CreateFile(
            Path.GetFullPath(path),
            0,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            flags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var capacity = 512;
        while (true)
        {
            var builder = new StringBuilder(capacity);
            var length = NativeMethods.GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (length < builder.Capacity)
            {
                return NormalizeDevicePath(builder.ToString());
            }

            capacity = checked((int)length + 1);
        }
    }

    private static string NormalizeDevicePath(string value)
    {
        if (value.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            return "\\\\" + value[8..];
        }

        return value.StartsWith("\\\\?\\", StringComparison.Ordinal) ? value[4..] : value;
    }

    private static class NativeMethods
    {
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint FileShareDelete = 0x00000004;
        public const uint OpenExisting = 3;
        public const uint FileFlagBackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            int filePathLength,
            uint flags);
    }
}

public sealed record VendorValidationResult(bool IsValid, string OperationCode, UpstreamRecord? Record)
{
    public static VendorValidationResult Success(UpstreamRecord record) => new(true, "VENDOR_OK", record);

    public static VendorValidationResult Failure(string operationCode) => new(false, operationCode, null);
}
