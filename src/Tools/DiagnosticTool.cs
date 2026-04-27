using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>Runs a build or lint command and returns structured diagnostic output.</summary>
public sealed class DiagnosticTool : IToolHandler
{
    // MSBuild format: path(line,col): severity code: message
    private static readonly Regex MsBuildRegex = new(
        @"^(.+)\((\d+),(\d+)\):\s+(error|warning|info)\s+(\w+):\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Name => "diagnostic";
    public string Description => "Run a build or lint command and return structured diagnostics";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "command" },
        properties = new
        {
            command = new { type = "string", description = "The build or lint command to run" },
            timeout_seconds = new { type = "integer", description = "Timeout in seconds (default: 60)" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("'command' is required.");

        var timeout = TimeSpan.FromSeconds(60);
        if (args.TryGetValue("timeout_seconds", out var ts) && int.TryParse(ts, out var secs) && secs > 0)
            timeout = TimeSpan.FromSeconds(secs);

        // Apply blocklist check before executing
        if (BashTool.IsBlocked(command))
            return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(
                new DiagnosticResult
                {
                    ExitCode = -1,
                    Diagnostics = [new DiagnosticMessage { Severity = "error", Message = $"Command blocked for safety: '{command}'" }]
                }));

        var (rawOutput, exitCode) = BashTool.RunShellWithExitCode(command, timeout);

        var diagnostics = ParseMsBuild(rawOutput);
        if (diagnostics.Count == 0)
            diagnostics = TryParseEslintJson(rawOutput);

        // Fallback: if no parseable diagnostics and command actually failed
        if (diagnostics.Count == 0 && exitCode != 0)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                File = "",
                Line = 0,
                Column = 0,
                Severity = "error",
                Code = "",
                Message = rawOutput.Length > 2000 ? rawOutput[..2000] + "..." : rawOutput
            });
        }

        var result = new DiagnosticResult { ExitCode = exitCode, Diagnostics = diagnostics };
        return Task.FromResult(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
    }

    internal static List<DiagnosticMessage> ParseMsBuild(string output)
    {
        var results = new List<DiagnosticMessage>();
        foreach (var line in output.Split('\n'))
        {
            var m = MsBuildRegex.Match(line.Trim());
            if (!m.Success) continue;
            results.Add(new DiagnosticMessage
            {
                File = m.Groups[1].Value.Trim(),
                Line = int.TryParse(m.Groups[2].Value, out var l) ? l : 0,
                Column = int.TryParse(m.Groups[3].Value, out var c) ? c : 0,
                Severity = m.Groups[4].Value.ToLowerInvariant(),
                Code = m.Groups[5].Value,
                Message = m.Groups[6].Value.Trim()
            });
        }
        return results;
    }

    internal static List<DiagnosticMessage> TryParseEslintJson(string output)
    {
        var results = new List<DiagnosticMessage>();
        try
        {
            // ESLint JSON output is an array of file results
            var trimmed = output.Trim();
            if (!trimmed.StartsWith("[")) return results;

            using var doc = JsonDocument.Parse(trimmed);
            foreach (var fileResult in doc.RootElement.EnumerateArray())
            {
                var filePath = fileResult.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
                if (!fileResult.TryGetProperty("messages", out var messages)) continue;

                foreach (var msg in messages.EnumerateArray())
                {
                    results.Add(new DiagnosticMessage
                    {
                        File = filePath,
                        Line = msg.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var lnVal) ? lnVal : 0,
                        Column = msg.TryGetProperty("column", out var col) && col.TryGetInt32(out var colVal) ? colVal : 0,
                        Severity = msg.TryGetProperty("severity", out var sev) && sev.TryGetInt32(out var sevVal)
                            ? (sevVal == 2 ? "error" : "warning") : "warning",
                        Code = msg.TryGetProperty("ruleId", out var rule) ? rule.GetString() ?? "" : "",
                        Message = msg.TryGetProperty("message", out var m) ? m.GetString() ?? "" : ""
                    });
                }
            }
        }
        catch { /* not ESLint JSON */ }
        return results;
    }
}

/// <summary>A single diagnostic message from a build or lint run.</summary>
public sealed class DiagnosticMessage
{
    [JsonPropertyName("file")] public string File { get; init; } = "";
    [JsonPropertyName("line")] public int Line { get; init; }
    [JsonPropertyName("column")] public int Column { get; init; }
    [JsonPropertyName("severity")] public string Severity { get; init; } = "error";
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

/// <summary>The structured result of a diagnostic tool run.</summary>
public sealed class DiagnosticResult
{
    [JsonPropertyName("exit_code")] public int ExitCode { get; init; }
    [JsonPropertyName("diagnostics")] public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}
