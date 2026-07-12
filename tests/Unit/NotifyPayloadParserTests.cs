using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class NotifyPayloadParserTests
{
    [Fact]
    public void Parses_valid_completion()
    {
        const string payload = """
            {"type":"agent-turn-complete","thread-id":"thread-1","turn-id":"turn-1","cwd":"C:\\work","input-messages":["secret"],"last-assistant-message":"secret"}
            """;

        var result = NotifyPayloadParser.Parse(payload);

        Assert.Equal(NotifyParseKind.Completion, result.Kind);
        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal("turn-1", result.TurnId);
    }

    [Fact]
    public void Ignores_other_event_types()
    {
        var result = NotifyPayloadParser.Parse("{\"type\":\"other\"}");

        Assert.Equal(NotifyParseKind.Ignored, result.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("line\nfeed")]
    [InlineData("nul\0value")]
    public void Rejects_invalid_opaque_ids(string value) => Assert.False(NotifyPayloadParser.IsValidOpaqueId(value));

    [Fact]
    public void Rejects_unpaired_surrogate()
    {
        var value = new string(['a', '\ud800', 'b']);

        Assert.False(NotifyPayloadParser.IsValidOpaqueId(value));
    }

    [Theory]
    [InlineData("{\"type\":\"agent-turn-complete\",\"type\":\"other\",\"thread-id\":\"t\",\"turn-id\":\"u\"}")]
    [InlineData("{\"type\":\"agent-turn-complete\",\"thread-id\":\"t\",\"thread-id\":\"other\",\"turn-id\":\"u\"}")]
    [InlineData("{\"type\":\"agent-turn-complete\",\"thread-id\":\"t\",\"turn-id\":\"u\",\"turn-id\":\"other\"}")]
    public void Rejects_duplicate_contract_properties(string payload)
    {
        Assert.Equal(NotifyParseKind.Invalid, NotifyPayloadParser.Parse(payload).Kind);
    }

    [Fact]
    public void Event_id_is_deterministic_and_partitioned()
    {
        var first = Hashing.EventId("00000000-0000-0000-0000-000000000001", "thread", "turn");
        var duplicate = Hashing.EventId("00000000-0000-0000-0000-000000000001", "thread", "turn");
        var otherMachine = Hashing.EventId("00000000-0000-0000-0000-000000000002", "thread", "turn");

        Assert.Equal(64, first.Length);
        Assert.Equal(first, duplicate);
        Assert.NotEqual(first, otherMachine);
    }
}
