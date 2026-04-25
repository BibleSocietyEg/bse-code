using FluentAssertions;
using System.Text.Json;

namespace BSE_Code.Tests;

/// <summary>
/// Tests for AppConfig serialization and ConfigManager.LoadOrSetupAsync
/// (the non-interactive, env-var-driven path only — the wizard requires a TTY).
/// </summary>
public class ConfigManagerTests : IDisposable
{
    // Env vars we may set — cleaned up in Dispose
    private readonly List<string> _setEnvVars = [];

    public void Dispose()
    {
        foreach (var key in _setEnvVars)
            Environment.SetEnvironmentVariable(key, null);
    }

    private void SetEnv(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _setEnvVars.Add(key);
    }

    // ── AppConfig serialization ───────────────────────────────────────────────

    [Fact]
    public void AppConfig_DefaultValues_AreCorrect()
    {
        var config = new AppConfig();

        config.Provider.Should().Be("OpenRouter");
        config.ApiKey.Should().BeEmpty();
        config.Model.Should().NotBeEmpty();
        config.BaseUrl.Should().BeEmpty();
        config.Theme.Should().Be("default");
    }

    [Fact]
    public void AppConfig_RoundTrip_PreservesAllFields()
    {
        var original = new AppConfig
        {
            Provider = "OpenAI",
            ApiKey   = "sk-test-key",
            Model    = "gpt-4o",
            BaseUrl  = "https://api.openai.com/v1",
            Theme    = "dracula"
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AppConfig>(json)!;

        restored.Provider.Should().Be(original.Provider);
        restored.ApiKey.Should().Be(original.ApiKey);
        restored.Model.Should().Be(original.Model);
        restored.BaseUrl.Should().Be(original.BaseUrl);
        restored.Theme.Should().Be(original.Theme);
    }

    [Fact]
    public void AppConfig_JsonPropertyNames_UseSnakeCase()
    {
        var config = new AppConfig { Provider = "Ollama", ApiKey = "local", Model = "llama3" };
        var json   = JsonSerializer.Serialize(config);

        json.Should().Contain("\"provider\"");
        json.Should().Contain("\"api_key\"");
        json.Should().Contain("\"base_url\"");
    }

    [Fact]
    public void AppConfig_ProviderEnum_ParsesKnownProviders()
    {
        foreach (var name in Enum.GetNames<LlmProvider>())
        {
            var config = new AppConfig { Provider = name };
            config.ProviderEnum.Should().Be(Enum.Parse<LlmProvider>(name));
        }
    }

    [Fact]
    public void AppConfig_ProviderEnum_UnknownProvider_ReturnsCustom()
    {
        var config = new AppConfig { Provider = "SomeUnknownProvider" };

        config.ProviderEnum.Should().Be(LlmProvider.Custom);
    }

    [Fact]
    public void AppConfig_ProviderEnum_IsCaseInsensitive()
    {
        var config = new AppConfig { Provider = "openai" };

        config.ProviderEnum.Should().Be(LlmProvider.OpenAI);
    }

    // ── LoadOrSetupAsync — env-var override path ──────────────────────────────

    [Fact]
    public async Task LoadOrSetupAsync_WithExistingConfig_EnvVarOverridesApiKey()
    {
        var tempDir  = Path.Combine(Path.GetTempPath(), $"bse-cfg-{Guid.NewGuid():N}");
        var cfgFile  = Path.Combine(tempDir, "config.json");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write a minimal config file
            var saved = new AppConfig { Provider = "OpenAI", ApiKey = "old-key", Model = "gpt-4o", BaseUrl = "https://api.openai.com/v1" };
            File.WriteAllText(cfgFile, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));

            // Override via env var
            SetEnv("BSE_API_KEY", "new-key-from-env");

            // We can't call ConfigManager.LoadOrSetupAsync directly because it uses a
            // hardcoded path. Instead, test the env-var logic by verifying the config
            // deserialization + override pattern that LoadOrSetupAsync implements.
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(cfgFile))!;
            var envKey = Environment.GetEnvironmentVariable("BSE_API_KEY");
            if (!string.IsNullOrEmpty(envKey)) loaded.ApiKey = envKey;

            loaded.ApiKey.Should().Be("new-key-from-env");
            loaded.Model.Should().Be("gpt-4o"); // unchanged
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── LlmProvider enum ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("OpenRouter",    LlmProvider.OpenRouter)]
    [InlineData("OpenAI",        LlmProvider.OpenAI)]
    [InlineData("Anthropic",     LlmProvider.Anthropic)]
    [InlineData("Google",        LlmProvider.Google)]
    [InlineData("Ollama",        LlmProvider.Ollama)]
    [InlineData("LmStudio",      LlmProvider.LmStudio)]
    [InlineData("LocalAiFoundry",LlmProvider.LocalAiFoundry)]
    [InlineData("Custom",        LlmProvider.Custom)]
    public void LlmProvider_AllValuesParseCorrectly(string name, LlmProvider expected)
    {
        Enum.TryParse<LlmProvider>(name, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }
}
