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
        if (oldStr.Length == 0)
            return "ERROR: 'old_str' must not be empty.";
        if (!args.TryGetValue("new_str", out var newStr) || newStr is null)
            throw new ArgumentException("'new_str' is required.");

        if (!File.Exists(filePath))
            return $"ERROR: '{filePath}': file not found";

        // Detect original encoding (preserves BOM if present)
        var encoding = DetectEncoding(filePath);
        var content = await File.ReadAllTextAsync(filePath, encoding);

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

        await File.WriteAllTextAsync(filePath, newContent, encoding);

        int linesDelta = Math.Abs(newStr.Count(c => c == '\n') - oldStr.Count(c => c == '\n'));
        return linesDelta == 0
            ? $"Edited '{filePath}': 1 line modified"
            : $"Edited '{filePath}': {linesDelta} line(s) added/removed";
    }

    private static System.Text.Encoding DetectEncoding(string filePath)
    {
        // Read the first 4 bytes to detect BOM
        using var fs = File.OpenRead(filePath);
        var bom = new byte[4];
        int read = fs.Read(bom, 0, 4);
        // Check UTF-32 BOMs first (4 bytes, more specific)
        if (read >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: true); // UTF-32 LE
        if (read >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return new System.Text.UTF32Encoding(bigEndian: true, byteOrderMark: true); // UTF-32 BE
        // Then check UTF-8 (3 bytes)
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        // Then check UTF-16 (2 bytes)
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return System.Text.Encoding.Unicode; // UTF-16 LE
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode; // UTF-16 BE
        return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false); // UTF-8 no BOM
    }
}
