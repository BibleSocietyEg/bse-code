/// <summary>
/// Abstraction for a named tool that the AI can invoke.
/// Follows the Open/Closed Principle — new tools are added by implementing
/// this interface rather than modifying a switch statement.
/// </summary>
public interface IToolHandler
{
    /// <summary>The tool name as registered with the AI (e.g. "read_file").</summary>
    string Name { get; }

    /// <summary>Human-readable description sent to the model.</summary>
    string Description { get; }

    /// <summary>JSON Schema object describing the tool's parameters.</summary>
    object ParameterSchema { get; }

    /// <summary>Executes the tool with the given JSON-encoded arguments.</summary>
    Task<string> ExecuteAsync(string argsJson);
}
