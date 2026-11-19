namespace TokenSaver.TerminalControl.Core;

/// <summary>
/// Line protocol shared with Scripts/tokensaver-tab.ps1.
/// Commands go in as "&lt;id&gt; &lt;command&gt;"; the tab writes "__TOKENSAVER_END__ &lt;id&gt; &lt;exitcode&gt;" after each command.
/// </summary>
public static class TabProtocol
{
    public const string EndMarker = "__TOKENSAVER_END__";
    public const string ErrorMarker = "__TOKENSAVER_ERR__";
    public const string CloseCommand = "__TOKENSAVER_CLOSE__";

    /// <summary>Parses "__TOKENSAVER_ERR__ &lt;id&gt; &lt;text&gt;" written in-band on the stdout pipe; null when the line is not an error line.</summary>
    public static TabErrorLine? ParseError(string line)
    {
        if (!line.StartsWith(ErrorMarker + " ", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = line[(ErrorMarker.Length + 1)..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        if (space < 1)
        {
            return null;
        }

        return new TabErrorLine(rest[..space], rest[(space + 1)..]);
    }

    public static string FormatCommand(string requestId, string command)
    {
        if (command.Contains('\n', StringComparison.Ordinal) || command.Contains('\r', StringComparison.Ordinal))
        {
            throw new ArgumentException("Commands must be a single line.", nameof(command));
        }

        return $"{requestId} {command}";
    }

    /// <summary>Parses "__TOKENSAVER_END__ &lt;id&gt; &lt;exitcode&gt;"; null when the line is not an end line.</summary>
    public static TabEndLine? ParseEnd(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != EndMarker)
        {
            return null;
        }

        if (!int.TryParse(parts[2], out var exitCode))
        {
            return null;
        }

        return new TabEndLine(parts[1], exitCode);
    }
}

/// <summary>A parsed "__TOKENSAVER_END__ &lt;id&gt; &lt;exitcode&gt;" line.</summary>
public readonly record struct TabEndLine(string RequestId, int ExitCode);

/// <summary>A parsed "__TOKENSAVER_ERR__ &lt;id&gt; &lt;text&gt;" line.</summary>
public readonly record struct TabErrorLine(string RequestId, string Text);
