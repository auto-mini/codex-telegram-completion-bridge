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
        Assert.Equal("secret", result.AnswerPreview);
    }

    [Fact]
    public void Retains_only_normalized_answer_prefix_and_never_input_messages()
    {
        const string payload = """
            {"type":"agent-turn-complete","thread-id":"t","turn-id":"u","input-messages":["PRIVATE PROMPT"],"last-assistant-message":"  12345678901234567890123456789012345678901234567890ANSWER TAIL"}
            """;
        Assert.Equal("12345678901234567890123456789012345678901234567890…", NotifyPayloadParser.Parse(payload).AnswerPreview);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"last-assistant-message\":null")]
    [InlineData(",\"last-assistant-message\":123")]
    [InlineData(",\"last-assistant-message\":[]")]
    [InlineData(",\"last-assistant-message\":\" \\n\\t\"")]
    [InlineData(",\"last-assistant-message\":\"first\",\"last-assistant-message\":\"second\"")]
    [InlineData(",\"last-assistant-message\":\"\\uD800\"")]
    public void Unusable_optional_answer_does_not_drop_completion(string property)
    {
        var result = NotifyPayloadParser.Parse("{\"type\":\"agent-turn-complete\",\"thread-id\":\"t\",\"turn-id\":\"u\"" + property + "}");
        Assert.Equal(NotifyParseKind.Completion, result.Kind);
        Assert.Null(result.AnswerPreview);
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
