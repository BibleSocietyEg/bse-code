/// <summary>Replaces the first exact occurrence of old_str with new_str in a file.</summary>
public sealed class EditFileTool : IToolHandler
{
    public string Name => "edit_file";
    public string Description => "Replace the first exact occurrence of old_str with new_str in a file";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "file_path", "old_str", "new_str" },
        properties = new
        {
            file_path = new { type = "string", description = "The path of the file to edit" },
            old_str = new { type = "string", description = "The exact string to replace" },
            new_str = new { type = "string", description = "The string to replace old_str with" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);

        if (!args.TryGetValue("file_path", out var filePath) || string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("'file_path' is required.");
        if (!args.TryGetValue("old_str", out var oldStr) || oldStr is null)
            throw new ArgumentException("'old_str' is required.");
        if (!args.TryGetValue("new_str", out var newStr) || newStr is null)
            throw new ArgumentException("'new_str' is required.");

        if (!File.Exists(filePath))
            return $"ERROR: '{filePath}': file not found";

        var content = await File.ReadAllTextAsync(filePath);

        // Count occurrences
        int count = 0;
        int searchFrom = 0;
        while (true)
        {
            int idx = content.IndexOf(oldStr, searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            count++;
            searchFrom = idx + oldStr.Length;
        }

        if (count == 0)
            return $"ERROR: old_str not found in '{filePath}'";
        if (count > 1)
            return $"ERROR: old_str is ambiguous — found {count} occurrences in '{filePath}'";

        // Replace only the first occurrence
        int firstIdx = content.IndexOf(oldStr, StringComparison.Ordinal);
        var newContent = content[..firstIdx] + newStr + content[(firstIdx + oldStr.Length)..];

        await File.WriteAllTextAsync(filePath, newContent);

        int linesChanged = Math.Abs(newStr.Count(c => c == '\n') - oldStr.Count(c => c == '\n')) + 1;
        return $"Edited '{filePath}': {linesChanged} line(s) changed";
    }
}
