namespace CodexTelegramCommon;

public static class RetryPolicy
{
    private static readonly TimeSpan[] ResolutionDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(300),
    ];

    private static readonly TimeSpan[] DeliveryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(300),
    ];

    public static TimeSpan ResolutionDelay(int completedAttempts)
    {
        if (completedAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        return completedAttempts < ResolutionDelays.Length
            ? ResolutionDelays[completedAttempts]
            : TimeSpan.FromMinutes(5);
    }

    public static TimeSpan DeliveryDelay(int completedAttempts)
    {
        if (completedAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        if (completedAttempts < DeliveryDelays.Length)
        {
            return DeliveryDelays[completedAttempts];
        }

        var exponent = Math.Min(completedAttempts - DeliveryDelays.Length, 6);
        return TimeSpan.FromMinutes(Math.Min(60, 5 * Math.Pow(2, exponent)));
    }
}
