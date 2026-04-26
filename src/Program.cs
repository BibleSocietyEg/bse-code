using System.ClientModel;
using System.Reflection;
using OpenAI;
using OpenAI.Chat;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Contains("--version") || args.Contains("-v"))
{
    var ver = typeof(ReplEngine).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    UI.Print($"bse-code {ver}", UI.Muted); return;
}

if (args.Contains("--help") || args.Contains("-h")) { ReplEngine.PrintHelp(); return; }
if (args.Contains("--config")) { await ConfigManager.LoadOrSetupAsync(forceReconfigure: true); return; }

var modelIdx = Array.IndexOf(args, "--model");
string? modelOverride = modelIdx >= 0 && modelIdx + 1 < args.Length ? args[modelIdx + 1] : null;
var themeIdx = Array.IndexOf(args, "--theme");
string? themeOverride = themeIdx >= 0 && themeIdx + 1 < args.Length ? args[themeIdx + 1] : null;
var fmtIdx = Array.IndexOf(args, "--output-format");
string outputFormat = fmtIdx >= 0 && fmtIdx + 1 < args.Length ? args[fmtIdx + 1].ToLowerInvariant() : "text";
var pIdx = Array.IndexOf(args, "-p");
string? inlinePrompt = pIdx >= 0 && pIdx + 1 < args.Length ? args[pIdx + 1] : null;

try { ReplEngine.ValidateUnknownFlags(args, inlinePrompt, modelOverride); }
catch (ArgumentException ex) { UI.Error(ex.Message); Environment.Exit(1); }

var config = await ConfigManager.LoadOrSetupAsync();
if (modelOverride is not null) config.Model = modelOverride;
ThemeManager.TrySet(themeOverride ?? config.Theme ?? "default");

MemoryManager.EnsureUserMemory(); MemoryManager.Reload();
SkillManager.EnsureDirectories(); SkillManager.Reload();
McpManager.EnsureExampleConfig(); await McpManager.LoadAsync();

var toolRegistry = ToolRegistry.CreateDefault(config);

ChatClient BuildClient() => new ChatClient(
    model: config.Model, credential: new ApiKeyCredential(config.ApiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(config.BaseUrl) });

var engine = new ReplEngine(config, toolRegistry, BuildClient,
    ReplEngine.BuildDefaultSystemPrompt, () => ReplEngine.BuildDefaultOptions(toolRegistry));

try
{
    if (inlinePrompt is not null) await engine.RunOneShotAsync(inlinePrompt, outputFormat);
    else await engine.RunAsync();
}
finally
{
    await McpManager.DisposeAsync();
}
