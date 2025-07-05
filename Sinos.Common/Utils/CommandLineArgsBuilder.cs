using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sinos.Utils;

public class CommandLineArgsBuilder
{
    private StringBuilder _stringBuilder;

    public CommandLineArgsBuilder()
    {
        this._stringBuilder = new();
    }

    public static CommandLineArgsBuilder Create() => new();

    private bool _isAppended = false;

    private void AppendDelimiter()
    {
        var sb = this._stringBuilder;
        if (sb.Length > 0)
        {
            sb.Append(' ');
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CommandLineArgsBuilder AppendShotArg(string argName, string? argValue = null)
    {
        var sb = this._stringBuilder;

        this.AppendDelimiter();
        sb.Append('-');
        sb.Append(argName);

        if (argValue != null)
            _ = this.AppendArg(argValue);

        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CommandLineArgsBuilder AppendArg(string arg)
    {
        var sb = this._stringBuilder;

        scoped var chars = arg.AsSpan();

        this.AppendDelimiter();

        if (string.IsNullOrEmpty(arg))
        {
            sb.Append("\"\"");
            return this;
        }

        // エスケープする文字がなければ
        int pre = 0;
        int cur = FindNextEscape(chars);
        if (cur < 0)
        {
            // エスケープ対象の文字が1つもなければエスケープせずに登録
            sb.Append(arg);
            return this;
        }

        sb.Append('"');

        while (cur >= 0)
        {
            if (pre < cur)
            {
                sb.Append(chars[pre..cur]);
            }

            char c = chars[cur];
            if (c == ' ')
                sb.Append(c);
            else if (c == '"')
                sb.Append("\\\"");
            else if (c == '\\')
                sb.Append("\\\\");

            // 次にエスケープが必要な文字位置を探す
            pre = cur + 1;
            cur = FindNextEscape(chars, pre);
        }

        if (pre < arg.Length)
        {
            // 最後の検出位置から末尾までを追記
            sb.Append(chars[pre..arg.Length]);
        }

        sb.Append('"');

        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindNextEscape(scoped ReadOnlySpan<char> text, int findIdx = 0)
    {
        for (int idx = findIdx; idx < text.Length; ++idx)
        {
            char c = text[idx];
            if (c is ' ' or '\"' or '\\')
            {
                return idx;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public override string ToString()
        => this._stringBuilder.ToString();
}
