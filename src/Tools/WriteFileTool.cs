/// <summary>Writes (or overwrites) a file with the given content.</summary>
public sealed class WriteFileTool : IToolHandler
{
    public string Name => "Write";
    public string Description => "Write content to a file";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "file_path", "content" },
        properties = new
        {
            file_path = new { type = "string", description = "The path of the file to write to" },
            content = new { type = "string", description = "The content to write to the file" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("file_path", out var filePath) || string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("'file_path' is required.");
        if (!args.TryGetValue("content", out var content))
            throw new ArgumentException("'content' is required.");

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(filePath, content);
        return "File written successfully.";
    }
}
