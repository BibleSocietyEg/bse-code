using System.Text;

/// <summary>Lists files and sub-directories at a given path.</summary>
public sealed class ListDirTool : IToolHandler
{
    public string Name => "list_dir";
    public string Description => "List files and directories at a path";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "path" },
        properties = new
        {
            path = new { type = "string", description = "Directory path to list" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        var path = args.GetValueOrDefault("path", ".");

        if (!Directory.Exists(path))
            return Task.FromResult($"Directory not found: {path}");

        var sb = new StringBuilder();
        foreach (var entry in Directory.GetFileSystemEntries(path).OrderBy(e => e))
        {
            var name = Path.GetFileName(entry);
            var isDir = Directory.Exists(entry);
            sb.AppendLine(isDir ? $"[DIR]  {name}/" : $"       {name}");
        }
        return Task.FromResult(sb.ToString());
    }
}
