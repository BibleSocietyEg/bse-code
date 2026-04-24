using System.Text;

/// <summary>
/// Accumulates streaming deltas for a single tool call until the stream completes.
/// The OpenAI streaming API delivers tool call data in fragments; this class
/// collects them into a coherent, usable record.
/// </summary>
internal sealed class ToolCallAccumulator
{
    private readonly StringBuilder _arguments = new();

    public string Id    { get; set; }
    public string Name  { get; set; }
    public int    Index { get; set; }

    /// <summary>The accumulated JSON argument string.</summary>
    public string Arguments => _arguments.ToString();

    public ToolCallAccumulator(string id, string name)
    {
        Id   = id;
        Name = name;
    }

    /// <summary>Appends a fragment to the argument buffer.</summary>
    public void AppendArguments(BinaryData? fragment)
    {
        if (fragment is not null)
            _arguments.Append(fragment.ToString());
    }
}
