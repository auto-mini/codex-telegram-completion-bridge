using System.Security.Cryptography;
using System.Text;

namespace CodexTelegramCommon;

public sealed class InstallationMutationLock : IDisposable
{
    private readonly Mutex mutex;
    private bool owns;

    public InstallationMutationLock(string userSid)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userSid))).ToLowerInvariant()[..16];
        mutex = new Mutex(initiallyOwned: false, $"Local\\CodexTelegramBridge-Mutation-{hash}");
    }

    public bool TryAcquire()
    {
        try
        {
            owns = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owns = true;
        }

        return owns;
    }

    public void Dispose()
    {
        if (owns)
        {
            mutex.ReleaseMutex();
            owns = false;
        }

        mutex.Dispose();
    }
}
