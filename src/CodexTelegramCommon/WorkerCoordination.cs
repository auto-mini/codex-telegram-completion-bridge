using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace CodexTelegramCommon;

public sealed class WorkerCoordination : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle wakeEvent;
    private bool ownsMutex;

    public WorkerCoordination(string machineId)
    {
        if (!Guid.TryParseExact(machineId, "D", out _))
        {
            throw new ArgumentException("Machine ID must be a canonical GUID.", nameof(machineId));
        }

        var names = BuildNames(machineId);
        mutex = new Mutex(initiallyOwned: false, names.MutexName);
        wakeEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, names.EventName);
    }

    public bool TryAcquire()
    {
        if (ownsMutex)
        {
            return true;
        }

        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public void Signal() => wakeEvent.Set();

    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var milliseconds = timeout >= TimeSpan.FromMilliseconds(int.MaxValue)
            ? Timeout.Infinite
            : Math.Max(0, (int)Math.Ceiling(timeout.TotalMilliseconds));
        var result = WaitHandle.WaitAny([wakeEvent, cancellationToken.WaitHandle], milliseconds);
        if (result == 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result == 0;
    }

    public void Dispose()
    {
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }

        wakeEvent.Dispose();
        mutex.Dispose();
    }

    public static void SignalExistingOrCreate(string machineId)
    {
        using var coordination = new WorkerCoordination(machineId);
        coordination.Signal();
    }

    internal static CoordinationNames BuildNames(string machineId)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
                  ?? string.Concat(Environment.UserDomainName, "\\", Environment.UserName);
        var sidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))).ToLowerInvariant()[..16];
        var stem = $"Local\\CodexTelegramBridge-{machineId}-{sidHash}";
        return new CoordinationNames($"{stem}-Mutex", $"{stem}-Wake");
    }
}

internal sealed record CoordinationNames(string MutexName, string EventName);
