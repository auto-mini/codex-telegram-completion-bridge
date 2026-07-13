namespace CodexTelegramCommon;

public sealed record EventRecord(
    string EventId,
    string MachineId,
    string ThreadId,
    string TurnId,
    DateTimeOffset ObservedAtUtc,
    CaptureMode IngestMode,
    EventState State,
    int ResolutionAttemptCount,
    int DeliveryAttemptCount,
    DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset? LeaseUntilUtc,
    string? LastErrorCode,
    DateTimeOffset? CompletedAtUtc,
    byte[]? DeliveryEnvelopeDpapi);

public sealed record HealthConditionRecord(
    string ConditionCode,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    DateTimeOffset? NotBeforeUtc);

public sealed record QueueCounts(
    long Pending,
    long Inflight,
    long Sent,
    long Shadow,
    long Suppressed,
    long Quarantine);

public sealed record ShadowRecord(long Sequence, string EventId, DateTimeOffset ObservedAtUtc);

public sealed record QuarantineRecord(
    long Sequence,
    string EventId,
    DateTimeOffset ObservedAtUtc,
    CaptureMode IngestMode,
    string ErrorCode);

public enum InsertOutcome
{
    Inserted,
    Duplicate,
    Busy,
}
