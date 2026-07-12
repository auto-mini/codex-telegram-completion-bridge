using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class CodexConfigEditorTests
{
    [Fact]
    public void Replaces_multiline_notify_without_reserializing_other_content()
    {
        const string source = "# keep\r\nnotify = [\r\n  \"old.exe\", # inline\r\n  \"turn-ended\",\r\n]\r\nmodel = \"gpt\"\r\n[features]\r\nflag = true\r\n";
        var document = CodexConfigDocument.Parse(Encoding.UTF8.GetBytes(source));

        var output = Encoding.UTF8.GetString(document.RenderWithNotify([@"C:\Program Files\Bridge\CodexTelegramBridge.exe", "hook"]));

        Assert.Contains("notify = [\"C:\\\\Program Files\\\\Bridge\\\\CodexTelegramBridge.exe\", \"hook\"]", output, StringComparison.Ordinal);
        Assert.Contains("# keep\r\n", output, StringComparison.Ordinal);
        Assert.EndsWith("model = \"gpt\"\r\n[features]\r\nflag = true\r\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("old.exe", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inserts_absent_notify_before_first_table_and_preserves_bom()
    {
        const string source = "# root comment\nmodel = \"gpt\"\n[features]\nflag = true\n";
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(source)).ToArray();
        var document = CodexConfigDocument.Parse(bytes);

        var output = document.RenderWithNotify(["bridge.exe", "hook"]);

        Assert.True(output.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = Encoding.UTF8.GetString(output[Encoding.UTF8.Preamble.Length..]);
        Assert.Equal("# root comment\nmodel = \"gpt\"\nnotify = [\"bridge.exe\", \"hook\"]\n[features]\nflag = true\n", text);
    }

    [Fact]
    public void Removes_notify_without_touching_neighboring_content()
    {
        const string source = "notify = [\"vendor.exe\", \"turn-ended\"] # preserve nothing from bridge line\nmodel = \"gpt\"\n";
        var document = CodexConfigDocument.Parse(source);

        var output = Encoding.UTF8.GetString(document.RenderWithNotify(null));

        Assert.Equal("model = \"gpt\"\n", output);
    }

    [Theory]
    [InlineData("notify = \"not-an-array\"\n")]
    [InlineData("notify = []\n")]
    [InlineData("notify = [\"one\", 2]\n")]
    [InlineData("notify = [\"one\"]\nnotify = [\"two\"]\n")]
    [InlineData("notify = [\"unterminated\"\n")]
    public void Rejects_invalid_or_ambiguous_notify(string source)
    {
        Assert.Throws<InvalidDataException>(() => CodexConfigDocument.Parse(source));
    }

    [Fact]
    public void Quoted_top_level_notify_is_recognized()
    {
        var document = CodexConfigDocument.Parse("\"notify\" = [\"vendor.exe\", \"turn-ended\"]\n");

        Assert.Equal(["vendor.exe", "turn-ended"], document.NotifyArgv);
    }

    [Fact]
    public void Notify_inside_table_is_not_top_level()
    {
        var document = CodexConfigDocument.Parse("[section]\nnotify = [\"inner\"]\n");

        Assert.Null(document.NotifyArgv);
        var output = Encoding.UTF8.GetString(document.RenderWithNotify(["bridge.exe", "hook"]));
        Assert.StartsWith("notify = [\"bridge.exe\", \"hook\"]\n[section]", output, StringComparison.Ordinal);
    }
}
