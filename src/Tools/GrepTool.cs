using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Searches for a regex pattern across files.
/// Fixes the original implementation which used plain <c>string.Contains</c>
/// despite advertising regex support.
/// </summary>
public sealed class GrepTool : IToolHandler
{
    private const int MaxMatches = 200;

    public string Name        => "grep";
    public string Description => "Search for a regex pattern in files";
    public object ParameterSchema => new
    {
        type     = "object",
        required = new[] { "pattern" },
        properties = new
        {
            pattern   = new { type = "string", description = "Regex pattern to search for" },
            path      = new { type = "string", description = "File or directory to search in (default: cwd)" },
            recursive = new { type = "boolean", description = "Search recursively (default: true)" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args       = ArgumentParser.ParseStringMap(argsJson);
        var pattern    = args["pattern"];
        var searchPath = args.GetValueOrDefault("path", Directory.GetCurrentDirectory());
        var recursive  = !args.TryGetValue("recursive", out var r) || r != "false";

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult($"Invalid regex pattern: {ex.Message}");
        }

        var files = File.Exists(searchPath)
            ? [searchPath]
            : Directory.GetFiles(
                searchPath, "*.*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        var sb         = new StringBuilder();
        int matchCount = 0;
        var cwd        = Directory.GetCurrentDirectory();

        foreach (var file in files)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!regex.IsMatch(lines[i])) continue;

                    sb.AppendLine($"{Path.GetRelativePath(cwd, file)}:{i + 1}: {lines[i].Trim()}");
                    matchCount++;

                    if (matchCount >= MaxMatches)
                    {
                        sb.AppendLine($"... (truncated at {MaxMatches} matches)");
                        return Task.FromResult(sb.ToString());
                    }
                }
            }
            catch { /* skip unreadable files */ }
        }

        return Task.FromResult(matchCount == 0 ? "No matches found." : sb.ToString());
    }
}
