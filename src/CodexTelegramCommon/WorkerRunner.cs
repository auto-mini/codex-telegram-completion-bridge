namespace CodexTelegramCommon;

public sealed class WorkerRunner(
    IWorkerIterationProcessor engine,
    RuntimeConfigStore configStore,
    Func<DateTimeOffset> utcNow,
    TimeSpan? idleTimeout = null,
    TimeSpan? maximumWait = null)
{
    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // A Windows mutex must be released by the thread that acquired it. The worker is
        // a dedicated process, so keep orchestration on this thread and synchronously wait
        // for individual asynchronous I/O operations while the mutex lease is held.
        return Task.FromResult(Run(cancellationToken));
    }

    private int Run(CancellationToken cancellationToken)
    {
        RuntimeConfig config;
        try
        {
            config = configStore.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 3;
        }

        using var coordination = new WorkerCoordination(config.MachineId);
        if (!coordination.TryAcquire())
        {
            coordination.Signal();
            return 0;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            WorkerIterationResult result;
            try
            {
                result = engine.ProcessOneAsync(cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            if (result.Kind == WorkerIterationKind.Processed)
            {
                continue;
            }

            if (result.Kind == WorkerIterationKind.Blocked)
            {
                return 0;
            }

            if (result.NextDueUtc is null)
            {
                bool signaled;
                try
                {
                    signaled = coordination.Wait(idleTimeout ?? TimeSpan.FromSeconds(60), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return 0;
                }

                if (signaled)
                {
                    continue;
                }

                return 0;
            }

            var delay = result.NextDueUtc.Value - utcNow();
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                var cap = maximumWait ?? TimeSpan.FromMinutes(5);
                coordination.Wait(delay > cap ? cap : delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
        }

        return 0;
    }
}
