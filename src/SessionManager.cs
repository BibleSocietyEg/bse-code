using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.Chat;

// ── Session model ─────────────────────────────────────────────────────────────

/// <summary>
/// A serializable representation of a chat message for session persistence.
/// </summary>
public class SavedMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

/// <summary>
/// A saved conversation session.
/// </summary>
public class SavedSession
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("saved_at")]
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<SavedMessage> Messages { get; set; } = [];
}

// ── Manager ───────────────────────────────────────────────────────────────────

/// <summary>
/// Manages saving and resuming conversation sessions.
/// Sessions are stored in ~/.bse-code/sessions/<project-hash>/
/// </summary>
public static class SessionManager
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code", "sessions");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string ProjectDir
    {
        get
        {
            var cwd  = Directory.GetCurrentDirectory();
            var hash = Math.Abs(cwd.GetHashCode()).ToString("x8");
            return Path.Combine(BaseDir, hash);
        }
    }

    /// <summary>Saves the current conversation with a tag.</summary>
    public static void Save(string tag, string model, List<ChatMessage> messages)
    {
        Directory.CreateDirectory(ProjectDir);

        var saved = new SavedSession
        {
            Tag     = tag,
            Model   = model,
            SavedAt = DateTime.UtcNow,
            Cwd     = Directory.GetCurrentDirectory(),
            Messages = messages
                .Where(m => m is UserChatMessage or AssistantChatMessage)
                .Select(m => new SavedMessage
                {
                    Role    = m is UserChatMessage ? "user" : "assistant",
                    Content = ExtractText(m)
                })
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .ToList()
        };

        var path = Path.Combine(ProjectDir, $"{Sanitize(tag)}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(saved, JsonOpts));
    }

    /// <summary>Lists all saved sessions for the current project.</summary>
    public static List<SavedSession> List()
    {
        if (!Directory.Exists(ProjectDir)) return [];

        var sessions = new List<SavedSession>();
        foreach (var file in Directory.GetFiles(ProjectDir, "*.json"))
        {
            try
            {
                var json    = File.ReadAllText(file);
                var session = JsonSerializer.Deserialize<SavedSession>(json, JsonOpts);
                if (session is not null) sessions.Add(session);
            }
            catch { /* skip corrupt files */ }
        }

        return sessions.OrderByDescending(s => s.SavedAt).ToList();
    }

    /// <summary>Loads a saved session by tag and returns its messages.</summary>
    public static List<ChatMessage>? Resume(string tag, out SavedSession? meta)
    {
        meta = null;
        var path = Path.Combine(ProjectDir, $"{Sanitize(tag)}.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json    = File.ReadAllText(path);
            var session = JsonSerializer.Deserialize<SavedSession>(json, JsonOpts);
            if (session is null) return null;

            meta = session;
            return session.Messages.Select(m => m.Role == "user"
                ? (ChatMessage)new UserChatMessage(m.Content)
                : new AssistantChatMessage(m.Content)).ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes a saved session by tag.</summary>
    public static bool Delete(string tag)
    {
        var path = Path.Combine(ProjectDir, $"{Sanitize(tag)}.json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string ExtractText(ChatMessage msg)
    {
        if (msg is UserChatMessage u)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in u.Content)
                if (part.Text is not null) sb.Append(part.Text);
            return sb.ToString();
        }
        if (msg is AssistantChatMessage a)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in a.Content)
                if (part.Text is not null) sb.Append(part.Text);
            return sb.ToString();
        }
        return "";
    }

    private static string Sanitize(string tag) =>
        string.Concat(tag.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
}
