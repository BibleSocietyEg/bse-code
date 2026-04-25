using System.Text.RegularExpressions;

namespace BSE_Code.Tools;

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

            // Simple exact match replacement first
            if (content.Contains(oldStr))
            {
                var newContent = content.Replace(oldStr, newStr);
                await File.WriteAllTextAsync(path, newContent);
                return $"Successfully edited '{path}'";
            }

            // If exact match fails, try normalization (whitespace/newlines)
            var normalizedContent = Normalize(content);
            var normalizedOldStr = Normalize(oldStr);

            if (normalizedContent.Contains(normalizedOldStr))
            {
                // This is trickier because we need to replace in the original content
                // to preserve original formatting elsewhere.
                // We'll use a regex that ignores whitespace differences.
                var regexPattern = Regex.Escape(oldStr);
                regexPattern = Regex.Replace(regexPattern, @"\s+", @"\s+");
                var regex = new Regex(regexPattern);

                if (regex.IsMatch(content))
                {
                    var newContent = regex.Replace(content, newStr, 1); // Only replace first occurrence
                    await File.WriteAllTextAsync(path, newContent);
                    return $"Successfully edited '{path}' (matched with whitespace normalization)";
                }
            }

            return $"Error: Could not find the exact 'old_str' in '{path}'. Make sure you provide a unique and exact contiguous block of code.";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to edit file: {ex.Message}";
        }
    }

    private static string Normalize(string input) =>
        Regex.Replace(input, @"\s+", " ").Trim();
}
