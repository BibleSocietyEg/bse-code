using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// ── Provider enum ─────────────────────────────────────────────────────────────

/// <summary>
/// Supported LLM provider backends.
/// </summary>
public enum LlmProvider
{
    OpenRouter,
    OpenAI,
    Anthropic,
    Google,
    Ollama,
    LmStudio,
    LocalAiFoundry,
    Custom
}

// ── Config model ──────────────────────────────────────────────────────────────

/// <summary>
/// Application configuration persisted to ~/.bse-code/config.json.
/// Properties can be overridden by environment variables at runtime.
/// </summary>
public class AppConfig
{
    /// <summary>The LLM provider backend (e.g. "OpenRouter", "Ollama").</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "OpenRouter";

    /// <summary>Encrypted API key (stored on disk). Never contains plaintext.</summary>
    [JsonPropertyName("api_key_encrypted")]
    public string ApiKeyEncrypted { get; set; } = "";

    /// <summary>Config version: 1 = legacy plaintext api_key, 2 = encrypted.</summary>
    [JsonPropertyName("config_version")]
    public int ConfigVersion { get; set; } = 1;

    /// <summary>API key for authentication (runtime only — never serialized).</summary>
    [JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>Model identifier (e.g. "gpt-4o", "llama3", "google/gemini-2.5-pro-exp-03-25:free").</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "z-ai/glm-4.5-air:free";

    /// <summary>OpenAI-compatible API base URL (set automatically by the setup wizard).</summary>
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "";

    /// <summary>Active color theme name.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "default";

    /// <summary>Parsed provider enum (derived from Provider string).</summary>
    [JsonIgnore]
    public LlmProvider ProviderEnum =>
        Enum.TryParse<LlmProvider>(Provider, ignoreCase: true, out var p) ? p : LlmProvider.Custom;
}

// ── Model entry ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single model available from a provider.
/// </summary>
/// <param name="Id">The model identifier used in API calls (e.g. "gpt-4o").</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="IsFree">Whether the model has zero prompt cost.</param>
public record ModelEntry(string Id, string Name, bool IsFree);

// ── Manager ───────────────────────────────────────────────────────────────────

/// <summary>
/// Manages application configuration: loading from disk, persisting changes,
/// and running the interactive first-run setup wizard.
/// Config file location: ~/.bse-code/config.json
/// </summary>
public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── Provider definitions ──────────────────────────────────────────────────

    private record ProviderDef(
        int Number,
        LlmProvider Provider,
        string Label,
        string Description,
        bool NeedsApiKey,
        string DefaultBaseUrl,
        string DefaultModel,
        string ApiKeyUrl = "");

    private static readonly ProviderDef[] Providers =
    [
        new(1, LlmProvider.OpenRouter,    "OpenRouter",       "100+ models, free tier available",          true,  "https://openrouter.ai/api/v1",  "google/gemini-2.5-pro-exp-03-25:free", "https://openrouter.ai/keys"),
        new(2, LlmProvider.OpenAI,        "OpenAI",           "GPT-4o, o3, and more",                      true,  "https://api.openai.com/v1",      "gpt-4o",                               "https://platform.openai.com/api-keys"),
        new(3, LlmProvider.Anthropic,     "Anthropic",        "Claude 3.5/3.7 Sonnet, Haiku",              true,  "https://api.anthropic.com/v1",   "claude-3-5-sonnet-20241022",           "https://console.anthropic.com/settings/keys"),
        new(4, LlmProvider.Google,        "Google AI",        "Gemini 2.5 Pro, Flash, and more",           true,  "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-pro-preview-05-06", "https://aistudio.google.com/app/apikey"),
        new(5, LlmProvider.Ollama,        "Ollama",           "Local models (llama3, mistral, qwen…)",     false, "http://localhost:11434/v1",       "llama3",                               ""),
        new(6, LlmProvider.LmStudio,      "LM Studio",        "Local models via LM Studio server",         false, "http://localhost:1234/v1",        "",                                     ""),
        new(7, LlmProvider.LocalAiFoundry,"Local AI Foundry", "Azure AI Foundry local inference",          false, "http://localhost:5272/v1",        "",                                     ""),
        new(8, LlmProvider.Custom,        "Custom / Other",   "Any OpenAI-compatible endpoint",            true,  "",                               "",                                     ""),
    ];

    // ── Curated fallback model lists per provider ─────────────────────────────

    private static readonly Dictionary<LlmProvider, List<(string Category, List<ModelEntry> Models)>> FallbackModels = new()
    {
        [LlmProvider.OpenRouter] =
        [
            ("Free Models", [
                new("google/gemini-2.5-pro-exp-03-25:free",   "Gemini 2.5 Pro (Google)",        true),
                new("google/gemini-2.0-flash-exp:free",        "Gemini 2.0 Flash (Google)",       true),
                new("meta-llama/llama-4-maverick:free",        "Llama 4 Maverick (Meta)",         true),
                new("meta-llama/llama-3.3-70b-instruct:free",  "Llama 3.3 70B Instruct (Meta)",   true),
                new("deepseek/deepseek-r1:free",               "DeepSeek R1 (671B)",              true),
                new("deepseek/deepseek-chat-v3-0324:free",     "DeepSeek Chat v3",                true),
                new("qwen/qwen3-coder-480b-a35b:free",         "Qwen3 Coder 480B",                true),
                new("qwen/qwen3-235b-a22b:free",               "Qwen3 235B",                      true),
                new("mistralai/mistral-small-3.1:free",        "Mistral Small 3.1",               true),
                new("nvidia/llama-3.1-nemotron-nano-8b-v1:free","Nemotron Nano 8B (NVIDIA)",      true),
                new("z-ai/glm-4.5-air:free",                   "GLM-4.5 Air (Z-AI)",              true),
            ]),
            ("Paid Models", [
                new("openai/gpt-4o",                           "GPT-4o (OpenAI)",                 false),
                new("openai/gpt-4o-mini",                      "GPT-4o Mini (OpenAI)",            false),
                new("openai/o3-mini",                          "o3-mini (OpenAI)",                false),
                new("anthropic/claude-3.5-sonnet",             "Claude 3.5 Sonnet (Anthropic)",   false),
                new("anthropic/claude-3.7-sonnet",             "Claude 3.7 Sonnet (Anthropic)",   false),
                new("google/gemini-2.5-pro",                   "Gemini 2.5 Pro (Google)",         false),
                new("deepseek/deepseek-r1",                    "DeepSeek R1 (paid)",              false),
                new("mistralai/mistral-large",                 "Mistral Large",                   false),
                new("meta-llama/llama-4-maverick",             "Llama 4 Maverick (paid)",         false),
            ]),
        ],
        [LlmProvider.OpenAI] =
        [
            ("GPT-4 Series", [
                new("gpt-4o",                "GPT-4o",                false),
                new("gpt-4o-mini",           "GPT-4o Mini",           false),
                new("gpt-4-turbo",           "GPT-4 Turbo",           false),
            ]),
            ("o-Series (Reasoning)", [
                new("o3",                    "o3",                    false),
                new("o3-mini",               "o3-mini",               false),
                new("o1",                    "o1",                    false),
                new("o1-mini",               "o1-mini",               false),
            ]),
            ("GPT-3.5", [
                new("gpt-3.5-turbo",         "GPT-3.5 Turbo",         false),
            ]),
        ],
        [LlmProvider.Anthropic] =
        [
            ("Claude 3.7", [
                new("claude-3-7-sonnet-20250219", "Claude 3.7 Sonnet",  false),
            ]),
            ("Claude 3.5", [
                new("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet",  false),
                new("claude-3-5-haiku-20241022",  "Claude 3.5 Haiku",   false),
            ]),
            ("Claude 3", [
                new("claude-3-opus-20240229",     "Claude 3 Opus",      false),
                new("claude-3-haiku-20240307",    "Claude 3 Haiku",     false),
            ]),
        ],
        [LlmProvider.Google] =
        [
            ("Gemini 2.5", [
                new("gemini-2.5-pro-preview-05-06",  "Gemini 2.5 Pro",         false),
                new("gemini-2.5-flash-preview-05-20","Gemini 2.5 Flash",        false),
            ]),
            ("Gemini 2.0", [
                new("gemini-2.0-flash",              "Gemini 2.0 Flash",        false),
                new("gemini-2.0-flash-lite",         "Gemini 2.0 Flash Lite",   false),
            ]),
            ("Gemini 1.5", [
                new("gemini-1.5-pro",                "Gemini 1.5 Pro",          false),
                new("gemini-1.5-flash",              "Gemini 1.5 Flash",        false),
            ]),
        ],
        [LlmProvider.Ollama] =
        [
            ("Popular Models (must be pulled first)", [
                new("llama3.2",          "Llama 3.2 (3B)",          true),
                new("llama3.1",          "Llama 3.1 (8B)",          true),
                new("llama3.1:70b",      "Llama 3.1 (70B)",         true),
                new("qwen2.5-coder",     "Qwen 2.5 Coder (7B)",     true),
                new("qwen2.5-coder:32b", "Qwen 2.5 Coder (32B)",    true),
                new("deepseek-r1",       "DeepSeek R1 (7B)",        true),
                new("deepseek-r1:32b",   "DeepSeek R1 (32B)",       true),
                new("mistral",           "Mistral (7B)",             true),
                new("codellama",         "Code Llama (7B)",          true),
                new("phi4",              "Phi-4 (14B)",              true),
                new("gemma3",            "Gemma 3 (4B)",             true),
            ]),
        ],
        [LlmProvider.LmStudio] =
        [
            ("LM Studio (model name from your loaded model)", [
                new("local-model",       "Use the model ID shown in LM Studio", true),
            ]),
        ],
        [LlmProvider.LocalAiFoundry] =
        [
            ("Local AI Foundry (model name from your deployment)", [
                new("phi-4",             "Phi-4",                   true),
                new("phi-3.5-mini",      "Phi-3.5 Mini",            true),
            ]),
        ],
    };

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Loads configuration from disk, falling back to the interactive setup wizard
    /// if no config file exists or <paramref name="forceReconfigure"/> is true.
    /// Environment variables always take precedence over saved values.
    /// </summary>
    public static async Task<AppConfig> LoadOrSetupAsync(bool forceReconfigure = false)
    {
        // Env vars always win
        var envKey = Environment.GetEnvironmentVariable("BSE_API_KEY")
                       ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var envModel = Environment.GetEnvironmentVariable("BSE_MODEL")
                       ?? Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
        var envBase = Environment.GetEnvironmentVariable("BSE_BASE_URL")
                       ?? Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL");
        var envProvider = Environment.GetEnvironmentVariable("BSE_PROVIDER");

        if (!forceReconfigure && File.Exists(ConfigFile))
        {
            var saved = Load();
            if (!string.IsNullOrEmpty(envKey)) saved.ApiKey = envKey;
            if (!string.IsNullOrEmpty(envModel)) saved.Model = envModel;
            if (!string.IsNullOrEmpty(envBase)) saved.BaseUrl = envBase;
            if (!string.IsNullOrEmpty(envProvider)) saved.Provider = envProvider;
            ValidateBaseUrl(saved);
            return saved;
        }

        return await RunSetupWizardAsync(envKey, envModel, envBase, envProvider);
    }

    // ── Setup wizard ──────────────────────────────────────────────────────────

    private static async Task<AppConfig> RunSetupWizardAsync(
        string? envKey, string? envModel, string? envBase, string? envProvider)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║   🚀  BSE-Code  ·  First-run setup  🚀   ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();

        // ── Step 1: Pick provider ─────────────────────────────────────────────
        ProviderDef providerDef;
        if (!string.IsNullOrEmpty(envProvider)
            && Enum.TryParse<LlmProvider>(envProvider, ignoreCase: true, out var envParsed))
        {
            providerDef = Providers.First(p => p.Provider == envParsed);
            Console.WriteLine($"✅  Provider loaded from BSE_PROVIDER env var: {providerDef.Label}");
        }
        else
        {
            providerDef = PickProvider();
        }

        // ── Step 2: Base URL ──────────────────────────────────────────────────
        string baseUrl;
        if (!string.IsNullOrEmpty(envBase))
        {
            baseUrl = envBase;
            Console.WriteLine($"✅  Base URL loaded from env var: {baseUrl}");
        }
        else
        {
            baseUrl = PromptBaseUrl(providerDef);
        }

        // ── Step 3: API key ───────────────────────────────────────────────────
        string apiKey = "";
        if (providerDef.NeedsApiKey || providerDef.Provider == LlmProvider.Custom)
        {
            if (!string.IsNullOrEmpty(envKey))
            {
                Console.WriteLine($"✅  API key loaded from env var.");
                apiKey = envKey;
            }
            else
            {
                apiKey = PromptApiKey(providerDef);
            }
        }
        else
        {
            Console.WriteLine($"  ℹ️  No API key needed for {providerDef.Label} (local provider).");
            apiKey = "local"; // placeholder so the SDK doesn't reject empty credential
        }

        // ── Step 4: Model ─────────────────────────────────────────────────────
        string model;
        if (!string.IsNullOrEmpty(envModel))
        {
            Console.WriteLine($"✅  Model loaded from env var: {envModel}");
            model = envModel;
        }
        else
        {
            model = await PickModelAsync(providerDef, apiKey, baseUrl);
        }

        var config = new AppConfig
        {
            Provider = providerDef.Provider.ToString(),
            ApiKey = apiKey,
            Model = model,
            BaseUrl = baseUrl,
        };

        ValidateBaseUrl(config);
        Save(config);

        Console.WriteLine();
        Console.WriteLine($"🎉  Config saved to: {ConfigFile}");
        Console.WriteLine($"   Provider : {config.Provider}");
        Console.WriteLine($"   Model    : {config.Model}");
        Console.WriteLine($"   Base URL : {config.BaseUrl}");
        Console.WriteLine();
        PrintEnvHint(config);

        return config;
    }

    // ── Provider picker ───────────────────────────────────────────────────────

    private static ProviderDef PickProvider()
    {
        Console.WriteLine("  🌐 Choose your AI provider:\n");

        foreach (var p in Providers)
        {
            var local = !p.NeedsApiKey ? " (local, no API key)" : "";
            Console.ForegroundColor = p.NeedsApiKey ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.Write($"    [{p.Number}] {p.Label}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  — {p.Description}{local}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.Write($"  Select provider [1-{Providers.Length}] (default 1): ");
        var input = Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int choice)
            && choice >= 1 && choice <= Providers.Length)
        {
            return Providers[choice - 1];
        }

        return Providers[0]; // default: OpenRouter
    }

    // ── Base URL prompt ───────────────────────────────────────────────────────

    private static string PromptBaseUrl(ProviderDef def)
    {
        if (def.Provider == LlmProvider.Custom)
        {
            Console.Write("  Enter the API base URL (e.g. http://localhost:8080/v1): ");
            var url = Console.ReadLine()?.Trim() ?? "";
            return string.IsNullOrEmpty(url) ? "http://localhost:8080/v1" : url;
        }

        if (def.Provider is LlmProvider.Ollama or LlmProvider.LmStudio or LlmProvider.LocalAiFoundry)
        {
            Console.Write($"  Base URL [{def.DefaultBaseUrl}]: ");
            var url = Console.ReadLine()?.Trim() ?? "";
            return string.IsNullOrEmpty(url) ? def.DefaultBaseUrl : url;
        }

        // Cloud providers: use default silently
        return def.DefaultBaseUrl;
    }

    // ── API key prompt ────────────────────────────────────────────────────────

    private static string PromptApiKey(ProviderDef def)
    {
        Console.WriteLine();
        if (!string.IsNullOrEmpty(def.ApiKeyUrl))
            Console.WriteLine($"  🔑 Get your API key at: {def.ApiKeyUrl}");

        Console.Write($"  Enter your {def.Label} API key: ");
        var key = ReadSecret();

        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("❌ API key is required for this provider.");
            Environment.Exit(1);
        }

        return key;
    }

    // ── Model picker ──────────────────────────────────────────────────────────

    private static async Task<string> PickModelAsync(ProviderDef def, string apiKey, string baseUrl)
    {
        Console.WriteLine();

        List<(string Category, List<ModelEntry> Models)> categories;

        // Try live fetch for providers that support /models endpoint
        if (def.Provider is LlmProvider.OpenRouter or LlmProvider.Ollama)
        {
            Console.WriteLine($"  🤖 Fetching available models from {def.Label}...");
            categories = await FetchModelsAsync(def, apiKey, baseUrl);
        }
        else if (FallbackModels.TryGetValue(def.Provider, out var fallback))
        {
            categories = fallback;
        }
        else
        {
            // Custom / unknown: free-type
            return PromptCustomModel(def.DefaultModel);
        }

        // For local providers with no models loaded, prompt free-type
        if (categories.All(c => c.Models.Count == 0))
        {
            Console.WriteLine($"  ℹ️  No models found. Make sure {def.Label} is running.");
            return PromptCustomModel(def.DefaultModel);
        }

        Console.WriteLine();

        int index = 1;
        var flat = new List<ModelEntry>();

        foreach (var (category, models) in categories)
        {
            Console.ForegroundColor = category.Contains("Free") || category.Contains("Popular")
                ? ConsoleColor.Green : ConsoleColor.Yellow;
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

        var defaultChoice = string.IsNullOrEmpty(def.DefaultModel)
            ? "1"
            : flat.FindIndex(m => m.Id == def.DefaultModel) + 1 is int idx && idx > 0
                ? idx.ToString() : "1";

        Console.Write($"  Select a model [1-{flat.Count}] (default {defaultChoice}): ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || !int.TryParse(input, out int choice)
            || choice < 1 || choice > flat.Count)
        {
            if (!int.TryParse(defaultChoice, out choice)) choice = 1;
        }

        var selected = flat[choice - 1];
        Console.WriteLine($"  ✅  Selected: {selected.Name}");
        return selected.Id;
    }

    private static string PromptCustomModel(string defaultModel)
    {
        var hint = string.IsNullOrEmpty(defaultModel) ? "" : $" [{defaultModel}]";
        Console.Write($"  Enter model name{hint}: ");
        var input = Console.ReadLine()?.Trim() ?? "";
        return string.IsNullOrEmpty(input) ? (string.IsNullOrEmpty(defaultModel) ? "default" : defaultModel) : input;
    }

    // ── Live model fetch ──────────────────────────────────────────────────────

    private static async Task<List<(string Category, List<ModelEntry> Models)>> FetchModelsAsync(
        ProviderDef def, string apiKey, string baseUrl)
    {
        try
        {
            using var http = new HttpClient();
            if (!string.IsNullOrEmpty(apiKey) && apiKey != "local")
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            http.Timeout = TimeSpan.FromSeconds(10);

            var url = baseUrl.TrimEnd('/') + "/models";
            var json = await http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            // Ollama returns { "models": [...] }, OpenRouter returns { "data": [...] }
            JsonElement arr;
            if (doc.RootElement.TryGetProperty("data", out arr)) { /* OpenRouter */ }
            else if (doc.RootElement.TryGetProperty("models", out arr)) { /* Ollama */ }
            else return FallbackModels.GetValueOrDefault(def.Provider) ?? [];

            var free = new List<ModelEntry>();
            var paid = new List<ModelEntry>();

            foreach (var item in arr.EnumerateArray())
            {
                // OpenRouter: { id, name, pricing.prompt }
                // Ollama:     { name, model }
                string id, name;
                if (item.TryGetProperty("id", out var idEl))
                {
                    id = idEl.GetString() ?? "";
                    name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                }
                else if (item.TryGetProperty("model", out var modelEl))
                {
                    id = modelEl.GetString() ?? "";
                    name = item.TryGetProperty("name", out var n2) ? n2.GetString() ?? id : id;
                }
                else continue;

                bool isFree = def.Provider == LlmProvider.Ollama
                    || id.EndsWith(":free");

                if (!isFree && item.TryGetProperty("pricing", out var pricing)
                    && pricing.TryGetProperty("prompt", out var promptPrice))
                {
                    isFree = (promptPrice.GetString() ?? "1") == "0";
                }

                if (isFree) free.Add(new(id, name, true));
                else paid.Add(new(id, name, false));
            }

            free.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            paid.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (def.Provider == LlmProvider.Ollama)
            {
                Console.WriteLine($"  🎉 Found {free.Count} local model(s).");
                return [("Local Models", free)];
            }

            Console.WriteLine($"  🎉 Found {free.Count} free and {paid.Count} paid models.");
            return [("Free Models ✦ $0", free), ("Paid Models", paid)];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  ⚠️  Could not fetch models ({ex.GetType().Name}). Showing built-in list.");
            Console.ResetColor();
            return FallbackModels.GetValueOrDefault(def.Provider) ?? [];
        }
    }

    // ── URL validation ────────────────────────────────────────────────────────

    internal static void ValidateBaseUrl(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl)) return; // wizard will prompt
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _))
        {
            Console.Error.WriteLine(
                $"❌ Invalid base URL: '{config.BaseUrl}'\n" +
                $"   Fix it in ~/.bse-code/config.json or via the BSE_BASE_URL environment variable.");
            Environment.Exit(1);
        }
    }

    // ── Persist ───────────────────────────────────────────────────────────────

    private static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            config.ApiKeyEncrypted = EncryptApiKey(config.ApiKey);
            config.ConfigVersion = 2;
        }
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, JsonOpts));
    }

    /// <summary>Persists only the theme change to config.json.</summary>
    public static void SaveTheme(AppConfig config)
    {
        if (!File.Exists(ConfigFile)) return;
        try
        {
            var existing = Load();
            existing.Theme = config.Theme;
            Save(existing);
        }
        catch { /* best-effort */ }
    }

    private static AppConfig Load()
    {
        var json = File.ReadAllText(ConfigFile);
        var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

        if (config.ConfigVersion >= 2)
        {
            // Encrypted storage
            if (!string.IsNullOrEmpty(config.ApiKeyEncrypted))
            {
                try
                {
                    config.ApiKey = DecryptApiKey(config.ApiKeyEncrypted);
                }
                catch
                {
                    UI.Warn("⚠️  Failed to decrypt API key. Please re-run setup: bse-code --config");
                    config.ApiKey = "";
                }
            }
        }
        else
        {
            // Legacy v1: read api_key field directly via secondary deserialization
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("api_key", out var legacyKey))
                    config.ApiKey = legacyKey.GetString() ?? "";
            }
            catch { /* ignore */ }
        }

        return config;
    }

    // ── Encryption helpers ────────────────────────────────────────────────────

    private static string EncryptApiKey(string plaintext)
    {
        if (OperatingSystem.IsWindows())
            return EncryptWindows(plaintext);
        return EncryptAesGcm(plaintext);
    }

    private static string DecryptApiKey(string ciphertext)
    {
        if (OperatingSystem.IsWindows())
            return DecryptWindows(ciphertext);
        return DecryptAesGcm(ciphertext);
    }

    // Windows: DPAPI
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string EncryptWindows(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string DecryptWindows(string ciphertext)
    {
        var bytes = Convert.FromBase64String(ciphertext);
        var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
            bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(decrypted);
    }

    // macOS/Linux: AES-256-GCM
    private static byte[] DeriveKey()
    {
        var machineSecret = System.Text.Encoding.UTF8.GetBytes(
            Environment.MachineName + Environment.UserName);
        return System.Security.Cryptography.SHA256.HashData(machineSecret);
    }

    private static string EncryptAesGcm(string plaintext)
    {
        var key = DeriveKey();
        var nonce = new byte[System.Security.Cryptography.AesGcm.NonceByteSizes.MaxSize];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[System.Security.Cryptography.AesGcm.TagByteSizes.MaxSize];
        using var aes = new System.Security.Cryptography.AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        // Format: nonce(12) + tag(16) + ciphertext
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        ciphertext.CopyTo(result, nonce.Length + tag.Length);
        return Convert.ToBase64String(result);
    }

    private static string DecryptAesGcm(string ciphertextBase64)
    {
        var key = DeriveKey();
        var data = Convert.FromBase64String(ciphertextBase64);
        int nonceSize = System.Security.Cryptography.AesGcm.NonceByteSizes.MaxSize;
        int tagSize = System.Security.Cryptography.AesGcm.TagByteSizes.MaxSize;
        var nonce = data[..nonceSize];
        var tag = data[nonceSize..(nonceSize + tagSize)];
        var ciphertext = data[(nonceSize + tagSize)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new System.Security.Cryptography.AesGcm(key, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        Console.WriteLine("  ── 💡 Optional: set via environment variables instead ──");
        if (isWin)
        {
            Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('BSE_PROVIDER', '{config.Provider}', 'User')");
            if (!string.IsNullOrEmpty(config.ApiKey) && config.ApiKey != "local")
                Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('BSE_API_KEY', '<your-key>', 'User')");
            Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('BSE_MODEL', '{config.Model}', 'User')");
        }
        else
        {
            Console.WriteLine($"  export BSE_PROVIDER=\"{config.Provider}\"");
            if (!string.IsNullOrEmpty(config.ApiKey) && config.ApiKey != "local")
                Console.WriteLine($"  export BSE_API_KEY=\"<your-key>\"");
            Console.WriteLine($"  export BSE_MODEL=\"{config.Model}\"");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}
