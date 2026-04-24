/// <summary>Reads and returns the full contents of a file.</summary>
public sealed class ReadFileTool : IToolHandler
{
    public string Name        => "read_file";
    public string Description => "Read and return the contents of a file";
    public object ParameterSchema => new
    {
        type     = "object",
        required = new[] { "file_path" },
        properties = new
        {
            file_path = new { type = "string", description = "The path to the file to read" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("file_path", out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("'file_path' is required.");

        if (!File.Exists(path))
            return Task.FromResult($"File not found: {path}");

        return File.ReadAllTextAsync(path);
    }
}
