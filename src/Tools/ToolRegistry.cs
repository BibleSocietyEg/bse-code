using System.Text.Json;
using OpenAI.Chat;

/// <summary>
/// Central registry of all available tool handlers.
/// Follows the Open/Closed Principle — register new tools without touching
/// the dispatch logic.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public ToolRegistry(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns all registered tool names.</summary>
    public IEnumerable<string> ToolNames => _handlers.Keys;

    /// <summary>Builds the <see cref="ChatTool"/> list to pass to the OpenAI SDK.</summary>
    public IEnumerable<ChatTool> ToChatTools()
    {
        foreach (var handler in _handlers.Values)
        {
            yield return ChatTool.CreateFunctionTool(
                functionName: handler.Name,
                functionDescription: handler.Description,
                functionParameters: BinaryData.FromObjectAsJson(handler.ParameterSchema)
            );
        }
    }

    /// <summary>
    /// Dispatches a tool call by name.
    /// Returns an error string for unknown tools rather than throwing,
    /// so the AI can receive the error as a tool result.
    /// </summary>
    public async Task<string> ExecuteAsync(string toolName, string argsJson)
    {
        if (_handlers.TryGetValue(toolName, out var handler))
            return await handler.ExecuteAsync(argsJson);

        return $"Unknown tool: {toolName}";
    }

    /// <summary>Checks whether a tool with the given name is registered.</summary>
    public bool Contains(string toolName) =>
        _handlers.ContainsKey(toolName);

    /// <summary>Creates a registry pre-loaded with all built-in tools.</summary>
    public static ToolRegistry CreateDefault() => new(
    [
        new ReadFileTool(),
        new WriteFileTool(),
        new EditFileTool(),
        new BashTool(),
        new ListDirTool(),
        new GlobTool(),
        new GrepTool(),
    ]);
}
