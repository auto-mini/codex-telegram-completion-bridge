using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexTelegramCommon;

public sealed class UpstreamLauncher(VendorExecutableValidator validator)
{
    public UpstreamLaunchResult Launch(UpstreamRecord record, string originalNotifyJson)
    {
        if (record.Kind == UpstreamKind.Absent)
        {
            return new UpstreamLaunchResult(true, "UPSTREAM_ABSENT", null);
        }

        var validation = validator.ValidateCaptured(record);
        if (!validation.IsValid || validation.Record is null)
        {
            return new UpstreamLaunchResult(false, validation.OperationCode, null);
        }

        try
        {
            var argv = record.Argv.Concat([originalNotifyJson]).ToArray();
            var processId = NativeProcess.Start(record.Argv[0], argv, Environment.CurrentDirectory);
            return new UpstreamLaunchResult(true, "UPSTREAM_STARTED", processId);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or ArgumentException)
        {
            return new UpstreamLaunchResult(false, "UPSTREAM_START_FAILED", null);
        }
    }
}

public sealed record UpstreamLaunchResult(bool Success, string OperationCode, int? ProcessId);

internal static class NativeProcess
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public static int Start(string applicationPath, IReadOnlyList<string> argv, string currentDirectory)
    {
        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = 1,
        };
        using var nullHandle = CreateFile(
            "NUL",
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            ref security,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (nullHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var startup = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseStdHandles,
            StandardInput = nullHandle.DangerousGetHandle(),
            StandardOutput = nullHandle.DangerousGetHandle(),
            StandardError = nullHandle.DangerousGetHandle(),
        };
        var commandLine = new StringBuilder(WindowsCommandLine.Build(argv));
        if (!CreateProcess(
                applicationPath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: true,
                CreateNoWindow | CreateUnicodeEnvironment,
                IntPtr.Zero,
                currentDirectory,
                ref startup,
                out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            return checked((int)processInformation.ProcessId);
        }
        finally
        {
            CloseHandle(processInformation.Thread);
            CloseHandle(processInformation.Process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SecurityAttributes securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
