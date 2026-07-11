using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class WorkerCoordinationTests
{
    [Fact]
    public void Names_are_stable_and_do_not_expose_sid()
    {
        var machine = Guid.NewGuid().ToString("D");

        var first = WorkerCoordination.BuildNames(machine);
        var second = WorkerCoordination.BuildNames(machine);

        Assert.Equal(first, second);
        Assert.Contains(machine, first.MutexName, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, first.MutexName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mutex_is_single_owner_and_event_wakes_waiter()
    {
        var machine = Guid.NewGuid().ToString("D");
        using var first = new WorkerCoordination(machine);
        using var second = new WorkerCoordination(machine);
        Assert.True(first.TryAcquire());

        var secondAcquired = true;
        var contender = new Thread(() => secondAcquired = second.TryAcquire());
        contender.Start();
        Assert.True(contender.Join(TimeSpan.FromSeconds(1)));
        Assert.False(secondAcquired);

        second.Signal();
        Assert.True(first.Wait(TimeSpan.FromSeconds(1), CancellationToken.None));
    }
}
