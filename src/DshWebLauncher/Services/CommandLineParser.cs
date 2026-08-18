using System.Text;

namespace DshWebLauncher.Services;

public static class CommandLineParser
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var slashCount = 0;

        foreach (var character in commandLine)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '"')
            {
                current.Append('\\', slashCount / 2);
                if (slashCount % 2 == 0)
                {
                    inQuotes = !inQuotes;
                }
                else
                {
                    current.Append('"');
                }
                slashCount = 0;
                continue;
            }

            current.Append('\\', slashCount);
            slashCount = 0;
            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrent();
            }
            else
            {
                current.Append(character);
            }
        }

        current.Append('\\', slashCount);
        AddCurrent();
        return result;

        void AddCurrent()
        {
            if (current.Length == 0) return;
            result.Add(current.ToString());
            current.Clear();
        }
    }
}
