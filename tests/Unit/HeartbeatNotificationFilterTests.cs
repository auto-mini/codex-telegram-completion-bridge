using System.Text.Json;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class HeartbeatNotificationFilterTests
{
    [Theory]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>DONT_NOTIFY</decision><message>상태 변화 없음</message></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>DONT_NOTIFY</decision></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>NOTIFY</decision><message> </message></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>UNKNOWN</decision><message>text</message></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>NOTIFY</decision><decision>DONT_NOTIFY</decision><message>text</message></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>NOTIFY</decision><message>first</message><message>second</message></heartbeat>")]
    [InlineData("<heartbeat><decision>NOTIFY</decision><message>text</message></heartbeat>")]
    [InlineData("<heartbeat><automation_id>test</automation_id><decision>NOTIFY</decision><message>unfinished")]
    public void Silent_or_unusable_heartbeat_does_not_create_completion(string answer)
    {
        var result = Parse(answer);
        Assert.Equal(NotifyParseKind.Ignored, result.Kind);
        Assert.Equal("HEARTBEAT_SUPPRESSED", result.ErrorCode);
        Assert.Null(result.AnswerPreview);
    }

    [Fact]
    public void Notifying_heartbeat_extracts_human_message_before_truncation()
    {
        var message = new string('가', 60);
        var result = Parse("  <heartbeat>\n<automation_id>test</automation_id><decision>NOTIFY</decision><message>" + message + "</message></heartbeat>  ");
        Assert.Equal(NotifyParseKind.Completion, result.Kind);
        Assert.Equal(new string('가', 50) + "…", result.AnswerPreview);
    }

    [Fact]
    public void Extracts_entities_and_cdata_without_metadata()
    {
        var result = Parse("<heartbeat><automation_id>test</automation_id><decision>NOTIFY</decision><message>오류 &amp; 복구 <![CDATA[<작업> 완료]]></message></heartbeat>");
        Assert.Equal("오류 & 복구 <작업> 완료", result.AnswerPreview);
    }

    [Theory]
    [InlineData("DONT_NOTIFY")]
    [InlineData("정상 처리 완료. 오류는 없습니다.")]
    [InlineData("문서의 <heartbeat> 태그를 수정했습니다.")]
    [InlineData("````xml\n<heartbeat>example</heartbeat>\n````")]
    public void Ordinary_answers_are_not_suppressed_by_words_or_examples(string answer)
    {
        var result = Parse(answer);
        Assert.Equal(NotifyParseKind.Completion, result.Kind);
        Assert.Equal(TextNormalizer.NormalizeAnswerPreview(answer), result.AnswerPreview);
    }

    private static NotifyParseResult Parse(string answer) => NotifyPayloadParser.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["type"] = "agent-turn-complete", ["thread-id"] = "thread", ["turn-id"] = "turn", ["last-assistant-message"] = answer,
    }));
}
