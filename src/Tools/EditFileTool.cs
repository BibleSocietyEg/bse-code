using System.Text.RegularExpressions;

/// <summary>
/// Edits a file by replacing a contiguous block of text with a new block.
/// This "SEARCH/REPLACE" pattern is much more efficient than overwriting
/// entire files for large codebases.
/// </summary>
public sealed class EditFileTool : IToolHandler
{
    public string Name => "edit_file";
    public string Description => "Edit a file using a SEARCH/REPLACE block. The 'old_str' must be a unique, contiguous chunk of lines in the file.";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "file_path", "old_str", "new_str" },
        properties = new
        {
            file_path = new { type = "string", description = "The path to the file to edit" },
            old_str = new { type = "string", description = "The exact contiguous block of text to find in the file" },
            new_str = new { type = "string", description = "The text to replace the old_str with" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("file_path", out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("'file_path' is required.");
        if (!args.TryGetValue("old_str", out var oldStr))
            throw new ArgumentException("'old_str' is required.");
        if (!args.TryGetValue("new_str", out var newStr))
            throw new ArgumentException("'new_str' is required.");

        if (!File.Exists(path))
            return $"Error: File not found at '{path}'";

        try
        {
            var content = await File.ReadAllTextAsync(path);

            // Count exact occurrences to detect ambiguity before replacing
            int exactCount = CountOccurrences(content, oldStr);

            if (exactCount > 1)
                return $"Error: 'old_str' matches {exactCount} locations in '{path}'. Provide more context to make it unique.";

            if (exactCount == 1)
            {
                // Replace only the first (and only) occurrence
                int idx = content.IndexOf(oldStr, StringComparison.Ordinal);
                var newContent = string.Concat(content.AsSpan(0, idx), newStr, content.AsSpan(idx + oldStr.Length));
                await File.WriteAllTextAsync(path, newContent);
                return $"Successfully edited '{path}'";
            }

            // Exact match failed — try whitespace-normalised fallback
            var regexPattern = Regex.Escape(oldStr);
            regexPattern = Regex.Replace(regexPattern, @"\s+", @"\s+");
            var regex = new Regex(regexPattern, RegexOptions.None);
            var matches = regex.Matches(content);

            if (matches.Count > 1)
                return $"Error: 'old_str' (whitespace-normalised) matches {matches.Count} locations in '{path}'. Provide more context to make it unique.";

            if (matches.Count == 1)
            {
                var m = matches[0];
                var newContent = string.Concat(content.AsSpan(0, m.Index), newStr, content.AsSpan(m.Index + m.Length));
                await File.WriteAllTextAsync(path, newContent);
                return $"Successfully edited '{path}' (matched with whitespace normalization)";
            }

            return $"Error: Could not find 'old_str' in '{path}'. Make sure you provide a unique, exact contiguous block of code.";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to edit file: {ex.Message}";
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0, idx = 0;
        while ((idx = source.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }

}
