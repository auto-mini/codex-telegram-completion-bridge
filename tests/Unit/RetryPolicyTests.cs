using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void Resolution_schedule_matches_blueprint()
    {
        var expected = new[] { 0d, 0.5, 1, 2, 4, 8, 15, 30, 60, 120, 300, 300 };

        var actual = Enumerable.Range(0, expected.Length).Select(index => RetryPolicy.ResolutionDelay(index).TotalSeconds);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Delivery_schedule_caps_at_one_hour()
    {
        Assert.Equal([2d, 5, 15, 60, 300], Enumerable.Range(0, 5).Select(index => RetryPolicy.DeliveryDelay(index).TotalSeconds));
        Assert.Equal(TimeSpan.FromHours(1), RetryPolicy.DeliveryDelay(100));
    }
}
