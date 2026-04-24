using System.Text.Json;

/// <summary>
/// Centralises JSON argument parsing for tool handlers, eliminating the
/// repeated boilerplate pattern across every handler.
/// </summary>
internal static class ArgumentParser
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserialises <paramref name="argsJson"/> into a string-keyed dictionary.
    /// Throws <see cref="ArgumentException"/> on malformed input.
    /// </summary>
    public static Dictionary<string, string> ParseStringMap(string argsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson, Opts)
                   ?? throw new ArgumentException("Arguments must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid tool arguments: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserialises <paramref name="argsJson"/> into a mixed-value dictionary.
    /// </summary>
    public static Dictionary<string, JsonElement> ParseElementMap(string argsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson, Opts)
                   ?? throw new ArgumentException("Arguments must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid tool arguments: {ex.Message}", ex);
        }
    }
}
