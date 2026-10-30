namespace Airlock.Delivery.Tests.Support;

/// <summary>One Dockerfile instruction: its keyword in capitals, its arguments with continued lines joined, its line.</summary>
internal sealed record Instruction(string Keyword, string Arguments, int Line);

/// <summary>A build stage: its FROM's image, its name, and its instructions after the FROM.</summary>
internal sealed record Stage(string Image, string Name, IReadOnlyList<Instruction> Instructions)
{
    public IEnumerable<Instruction> All(string keyword) => Instructions.Where(instruction => instruction.Keyword == keyword);
}

internal static class DockerfileModel
{
    /// <summary>The instructions, with comments and blank lines left out and a trailing backslash joining lines.</summary>
    public static IReadOnlyList<Instruction> Instructions(string relativePath)
    {
        List<Instruction> instructions = [];
        var lines = Repository.Lines(relativePath);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var start = index + 1;
            var text = line;
            while (text.EndsWith('\\') && index + 1 < lines.Length)
            {
                text = text[..^1].TrimEnd() + " " + lines[++index].Trim();
            }

            var space = text.IndexOf(' ', StringComparison.Ordinal);
            instructions.Add(space < 0
                ? new Instruction(text.ToUpperInvariant(), string.Empty, start)
                : new Instruction(text[..space].ToUpperInvariant(), text[(space + 1)..].Trim(), start));
        }

        return instructions;
    }

    public static IReadOnlyList<Stage> Stages(string relativePath)
    {
        List<Stage> stages = [];
        List<Instruction>? current = null;
        foreach (var instruction in Instructions(relativePath))
        {
            if (instruction.Keyword == "FROM")
            {
                var words = instruction.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                current = [];
                stages.Add(new Stage(words[0], words.Length == 3 && words[1].Equals("AS", StringComparison.OrdinalIgnoreCase) ? words[2] : string.Empty, current));
            }
            else
            {
                (current ?? throw new InvalidOperationException($"{relativePath}: {instruction.Keyword} before any FROM")).Add(instruction);
            }
        }

        return stages;
    }
}
