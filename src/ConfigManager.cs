using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// ── Config model ──────────────────────────────────────────────────────────────

public class AppConfig
{
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "z-ai/glm-4.5-air:free";

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
}

// ── OpenRouter model list ─────────────────────────────────────────────────────

public record ModelEntry(string Id, string Name, bool IsFree);

// ── Manager ───────────────────────────────────────────────────────────────────

public static class ConfigManager
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // Curated fallback list shown when the API call fails
    private static readonly List<(string Category, List<ModelEntry> Models)> FallbackModels =
    [
        ("Free Models", [
            new("google/gemini-2.5-pro-exp-03-25:free",  "Gemini 2.5 Pro (Google)",       true),
            new("google/gemini-2.0-flash-exp:free",       "Gemini 2.0 Flash (Google)",      true),
            new("meta-llama/llama-4-maverick:free",       "Llama 4 Maverick (Meta)",        true),
            new("meta-llama/llama-3.3-70b-instruct:free", "Llama 3.3 70B Instruct (Meta)",  true),
            new("deepseek/deepseek-r1:free",              "DeepSeek R1 (671B)",             true),
            new("deepseek/deepseek-chat-v3-0324:free",    "DeepSeek Chat v3 (free)",        true),
            new("qwen/qwen3-coder-480b-a35b:free",        "Qwen3 Coder 480B",               true),
            new("qwen/qwen3-235b-a22b:free",              "Qwen3 235B",                     true),
            new("mistralai/mistral-small-3.1:free",       "Mistral Small 3.1",              true),
            new("nvidia/llama-3.1-nemotron-nano-8b-v1:free", "Nemotron Nano 8B (NVIDIA)",   true),
            new("z-ai/glm-4.5-air:free",                  "GLM-4.5 Air (Z-AI)",             true),
        ]),
        ("Paid Models", [
            new("openai/gpt-4o",                          "GPT-4o (OpenAI)",                false),
            new("openai/gpt-4o-mini",                     "GPT-4o Mini (OpenAI)",           false),
            new("openai/o3-mini",                         "o3-mini (OpenAI)",               false),
            new("anthropic/claude-3.5-sonnet",            "Claude 3.5 Sonnet (Anthropic)",  false),
            new("anthropic/claude-3.7-sonnet",            "Claude 3.7 Sonnet (Anthropic)",  false),
            new("google/gemini-2.5-pro",                  "Gemini 2.5 Pro (Google)",        false),
            new("deepseek/deepseek-r1",                   "DeepSeek R1 (paid)",             false),
            new("mistralai/mistral-large",                "Mistral Large",                  false),
            new("meta-llama/llama-4-maverick",            "Llama 4 Maverick (paid)",        false),
        ]),
    ];

    // ── Public entry point ────────────────────────────────────────────────────

    public static async Task<AppConfig> LoadOrSetupAsync(bool forceReconfigure = false)
    {
        // Env vars always win
        var envKey   = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var envModel = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
        var envBase  = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL");

        if (!forceReconfigure && File.Exists(ConfigFile))
        {
            var saved = Load();
            // Env vars override saved values
            if (!string.IsNullOrEmpty(envKey))   saved.ApiKey  = envKey;
            if (!string.IsNullOrEmpty(envModel)) saved.Model   = envModel;
            if (!string.IsNullOrEmpty(envBase))  saved.BaseUrl = envBase;
            return saved;
        }

        // First run or --config flag
        return await RunSetupWizardAsync(envKey, envModel, envBase);
    }

    // ── Setup wizard ──────────────────────────────────────────────────────────

    private static async Task<AppConfig> RunSetupWizardAsync(
        string? envKey, string? envModel, string? envBase)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║        BSE-Code  ·  First-run setup      ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();

        // ── API Key ───────────────────────────────────────────────────────────
        string apiKey;
        if (!string.IsNullOrEmpty(envKey))
        {
            Console.WriteLine($"✔  API key loaded from OPENROUTER_API_KEY env var.");
            apiKey = envKey;
        }
        else
        {
            Console.WriteLine("  Get your free API key at: https://openrouter.ai/keys");
            Console.Write("  Enter your OpenRouter API key: ");
            apiKey = ReadSecret();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("API key is required.");
                Environment.Exit(1);
            }
        }

        // ── Model selection ───────────────────────────────────────────────────
        string model;
        if (!string.IsNullOrEmpty(envModel))
        {
            Console.WriteLine($"✔  Model loaded from OPENROUTER_MODEL env var: {envModel}");
            model = envModel;
        }
        else
        {
            model = await PickModelAsync(apiKey, envBase ?? "https://openrouter.ai/api/v1");
        }

        var config = new AppConfig
        {
            ApiKey  = apiKey,
            Model   = model,
            BaseUrl = envBase ?? "https://openrouter.ai/api/v1"
        };

        Save(config);

        Console.WriteLine();
        Console.WriteLine($"✔  Config saved to: {ConfigFile}");
        Console.WriteLine($"   Model : {config.Model}");
        Console.WriteLine();
        PrintEnvHint(config);

        return config;
    }

    // ── Model picker ──────────────────────────────────────────────────────────

    private static async Task<string> PickModelAsync(string apiKey, string baseUrl)
    {
        Console.WriteLine();
        Console.WriteLine("  Fetching available models from OpenRouter...");

        var categories = await FetchModelsAsync(apiKey, baseUrl);

        Console.WriteLine();

        int index = 1;
        var flat  = new List<ModelEntry>();

        foreach (var (category, models) in categories)
        {
            Console.ForegroundColor = category.StartsWith("Free") ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"  ── {category} ──");
            Console.ResetColor();

            foreach (var m in models)
            {
                Console.WriteLine($"    [{index,2}] {m.Name}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"         {m.Id}");
                Console.ResetColor();
                flat.Add(m);
                index++;
            }
            Console.WriteLine();
        }

        Console.Write($"  Select a model [1-{flat.Count}] (default 1): ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || !int.TryParse(input, out int choice) || choice < 1 || choice > flat.Count)
            choice = 1;

        var selected = flat[choice - 1];
        Console.WriteLine($"  ✔  Selected: {selected.Name}");
        return selected.Id;
    }

    // ── Fetch live model list from OpenRouter ─────────────────────────────────

    private static async Task<List<(string Category, List<ModelEntry> Models)>> FetchModelsAsync(
        string apiKey, string baseUrl)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            http.Timeout = TimeSpan.FromSeconds(10);

            var url      = baseUrl.TrimEnd('/') + "/models";
            var json     = await http.GetStringAsync(url);
            var doc      = JsonDocument.Parse(json);
            var dataArr  = doc.RootElement.GetProperty("data");

            var free = new List<ModelEntry>();
            var paid = new List<ModelEntry>();

            foreach (var item in dataArr.EnumerateArray())
            {
                var id   = item.GetProperty("id").GetString() ?? "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;

                // Determine free: id ends with :free OR pricing prompt == "0"
                bool isFree = id.EndsWith(":free");
                if (!isFree && item.TryGetProperty("pricing", out var pricing)
                    && pricing.TryGetProperty("prompt", out var promptPrice))
                {
                    var priceStr = promptPrice.GetString() ?? "1";
                    isFree = priceStr == "0";
                }

                if (isFree) free.Add(new(id, name, true));
                else        paid.Add(new(id, name, false));
            }

            // Sort alphabetically within each group
            free.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            paid.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"  Found {free.Count} free and {paid.Count} paid models.");

            return [("Free Models ✦ $0", free), ("Paid Models", paid)];
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("  ⚠  Could not reach OpenRouter API — showing built-in model list.");
            Console.ResetColor();
            return FallbackModels;
        }
    }

    // ── Persist ───────────────────────────────────────────────────────────────

    private static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, JsonOpts));
    }

    private static AppConfig Load()
    {
        var json = File.ReadAllText(ConfigFile);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reads a line without echoing characters (for API keys).</summary>
    private static string ReadSecret()
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
            }
            else
            {
                sb.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return sb.ToString();
    }

    private static void PrintEnvHint(AppConfig config)
    {
        bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ── Optional: set via environment variables instead ──");
        if (isWin)
        {
            Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', '{config.ApiKey}', 'User')");
            Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('OPENROUTER_MODEL',   '{config.Model}',  'User')");
        }
        else
        {
            Console.WriteLine($"  export OPENROUTER_API_KEY=\"{config.ApiKey}\"");
            Console.WriteLine($"  export OPENROUTER_MODEL=\"{config.Model}\"");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}
