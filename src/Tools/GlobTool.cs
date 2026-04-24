/// <summary>Finds files matching a glob pattern under a base directory.</summary>
public sealed class GlobTool : IToolHandler
{
    public string Name        => "glob";
    public string Description => "Find files matching a glob pattern";
    public object ParameterSchema => new
    {
        type     = "object",
        required = new[] { "pattern" },
        properties = new
        {
            pattern   = new { type = "string", description = "Glob pattern, e.g. src/**/*.cs" },
            base_path = new { type = "string", description = "Base directory (default: cwd)" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args     = ArgumentParser.ParseStringMap(argsJson);
        var pattern  = args["pattern"];
        var basePath = args.GetValueOrDefault("base_path", Directory.GetCurrentDirectory());

        var files = Directory.GetFiles(basePath, pattern, SearchOption.AllDirectories);
        return Task.FromResult(
            files.Length == 0
                ? "No files matched."
                : string.Join("\n", files.Select(f => Path.GetRelativePath(basePath, f)))
        );
    }
}
