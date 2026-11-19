using System.Text;

namespace TokenSaver.Common.Core.Processes;

/// <summary>
///     Splits a command-line string into arguments for <see cref="CommandRequest.Arguments" />. Whitespace separates
///     arguments and double quotes group them (a quote inside a quoted argument is written <c>\"</c>); nothing else is
///     interpreted, so no shell syntax, variable or wildcard expansion ever happens.
/// </summary>
public static class CommandLine
{
    public static IReadOnlyList<string> Split(string? line)
    {
        List<string> arguments = [];
        if (string.IsNullOrWhiteSpace(line))
        {
            return arguments;
        }

        StringBuilder current = new();
        bool inQuotes = false;
        bool hasToken = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                _ = current.Append('"');
                i++;
                hasToken = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    arguments.Add(current.ToString());
                    _ = current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                _ = current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
