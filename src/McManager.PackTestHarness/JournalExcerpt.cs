using System.Text;
using McManager.Core.Setup;

namespace McManager.PackTestHarness;

internal static class JournalExcerpt
{
    public const int MaxLines = 80;

    public static string FromLines(IReadOnlyList<string> lines)
    {
        var hits = new List<string>();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!IsExcerptLine(raw))
                continue;
            var line = raw.TrimEnd('\r');
            if (line.Length > MinecraftReadiness.JournalLineMaxChars)
                line = line[..MinecraftReadiness.JournalLineMaxChars] + "…";
            hits.Add(line);
        }

        if (hits.Count > MaxLines)
            hits = hits.Skip(hits.Count - MaxLines).ToList();
        return string.Join('\n', hits);
    }

    public static void WritePhaseLogs(
        string phaseDir,
        string packId,
        IReadOnlyList<string> journalLines,
        PackTestResultDocument result)
    {
        var logsDir = Path.Combine(phaseDir, "logs");
        Directory.CreateDirectory(logsDir);
        var fullName = packId + ".journal.txt";
        var excerptName = packId + ".excerpt.txt";
        File.WriteAllText(Path.Combine(logsDir, fullName), Join(journalLines));
        var excerpt = FromLines(journalLines);
        File.WriteAllText(Path.Combine(logsDir, excerptName), excerpt);
        result.LogExcerptPath = "logs/" + excerptName;
    }

    private static bool IsExcerptLine(string line) =>
        line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Exception", StringComparison.OrdinalIgnoreCase);

    private static string Join(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.AppendLine(line.TrimEnd('\r'));
        return sb.ToString();
    }
}
