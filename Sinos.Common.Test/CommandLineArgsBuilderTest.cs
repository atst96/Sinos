using Sinos.Utils;

namespace Sinos.Share.Test;

public class CommandLineArgsBuilderTest
{
    [Fact]
    public void TestEmpty()
    {
        var expected = "";
        var actual = CommandLineArgsBuilder.Create()
            .ToString();

        Assert.Equal(expected, actual);
    }

    // FIXME: テストを細かく
    [Theory]
    [InlineData("name", null, "-name")]        // 値未指定
    [InlineData("name", "", "-name \"\"")]     // 値が空文字列
    [InlineData("name", "test", "-name test")] // 値がエスケープ不要
    [InlineData("name", " ", "-name \" \"")]   // 値がスペース単体
    [InlineData("name", "  ", "-name \"  \"")] // 値がスペース複数
    [InlineData("name", "\"", "-name \"\\\"\"")] // 値がダブルクォーテーション単体
    [InlineData("name", "\\", "-name \"\\\\\"")] // 値がバックスラッシュ単体
    public void TestSingleOption(string argName, string? argValue, string expected)
    {
        var actual = CommandLineArgsBuilder.Create()
            .AppendShotArg(argName, argValue)
            .ToString();

        Assert.Equal(expected, actual);
    }

    // FIXME: テストを細かく
    [Theory]
    [InlineData("cmd", "cmd")]         // エスケープなし
    [InlineData("", "\"\"")]           // 空文字列
    [InlineData(" ", "\" \"")]         // スペースのみ
    [InlineData("  ", "\"  \"")]       // スペースのみ
    [InlineData("\"", "\"\\\"\"")]     // escape: only double quote
    [InlineData("\\", "\"\\\\\"")]     // escape: back slash
    [InlineData(" cmd", "\" cmd\"")]   // 始端にエスケープ検出
    [InlineData("cmd ", "\"cmd \"")]   // 終端にエスケープ検出
    [InlineData("c m d", "\"c m d\"")] // 文字列の途中にエスケープ検出
    public void TestArg(string arg, string expected)
    {
        var actual = CommandLineArgsBuilder.Create()
            .AppendArg(arg)
            .ToString();

        Assert.Equal(expected, actual);
    }

    // FIXME: テストを細かく
    [Fact]
    public void TestOptArg1()
    {
        var actual = CommandLineArgsBuilder.Create()
            .AppendShotArg("arg1")
            .AppendArg("value a")
            .AppendArg("value-b")
            .AppendShotArg("arg2", "value")
            .AppendShotArg("arg3", "value c")
            .ToString();

        var expected = "-arg1 \"value a\" value-b -arg2 value -arg3 \"value c\"";

        Assert.Equal(expected, actual);
    }
}
