using System.Diagnostics;
using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class WorkerRunnerIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WorkerRunnerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Idle_worker_exits_after_configured_quiet_period()
    {
        var config = PrepareConfig();
        var processor = new SequenceProcessor(new WorkerIterationResult(WorkerIterationKind.Waiting));
        var runner = new WorkerRunner(processor, config.Store, () => DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();

        var exit = await Task.Run(() => runner.RunAsync(CancellationToken.None));

        Assert.Equal(0, exit);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task Named_event_wakes_idle_worker_without_missed_work()
    {
        var config = PrepareConfig();
        var firstCall = new ManualResetEventSlim();
        var processor = new SequenceProcessor(
            new WorkerIterationResult(WorkerIterationKind.Waiting),
            new WorkerIterationResult(WorkerIterationKind.Blocked))
        {
            OnCall = call =>
            {
                if (call == 1)
                {
                    firstCall.Set();
                }
            },
        };
        var runner = new WorkerRunner(processor, config.Store, () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        var running = Task.Run(() => runner.RunAsync(CancellationToken.None));
        Assert.True(firstCall.Wait(TimeSpan.FromSeconds(1)));

        WorkerCoordination.SignalExistingOrCreate(config.Value.MachineId);
        var exit = await running.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, exit);
        Assert.Equal(2, processor.Calls);
    }

    [Fact]
    public async Task Cancellation_during_wait_is_a_clean_exit()
    {
        var config = PrepareConfig();
        var processor = new SequenceProcessor(new WorkerIterationResult(WorkerIterationKind.Waiting));
        var runner = new WorkerRunner(processor, config.Store, () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var exit = await Task.Run(() => runner.RunAsync(cancellation.Token));

        Assert.Equal(0, exit);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private (RuntimeConfig Value, RuntimeConfigStore Store) PrepareConfig()
    {
        Directory.CreateDirectory(root);
        var value = new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = Path.Combine(root, ".codex"),
        };
        var store = new RuntimeConfigStore(Path.Combine(root, "bridge.json"));
        store.Save(value);
        return (value, store);
    }

    private sealed class SequenceProcessor(params WorkerIterationResult[] results) : IWorkerIterationProcessor
    {
        private readonly Queue<WorkerIterationResult> results = new(results);
        public int Calls { get; private set; }
        public Action<int>? OnCall { get; init; }

        public Task<WorkerIterationResult> ProcessOneAsync(CancellationToken cancellationToken)
        {
            Calls++;
            OnCall?.Invoke(Calls);
            return Task.FromResult(results.Count > 0
                ? results.Dequeue()
                : new WorkerIterationResult(WorkerIterationKind.Blocked));
        }
    }
}
