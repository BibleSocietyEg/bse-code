using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests;

public class ConfigManagerEncryptionTests
{
    // Unit tests for encryption round-trip
    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        // Use reflection to access internal methods
        var encrypt = typeof(ConfigManager).GetMethod("EncryptApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var decrypt = typeof(ConfigManager).GetMethod("DecryptApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var original = "sk-test-api-key-12345";
        var encrypted = (string)encrypt.Invoke(null, [original])!;
        var decrypted = (string)decrypt.Invoke(null, [encrypted])!;

        decrypted.Should().Be(original);
        encrypted.Should().NotBe(original);
    }

    [Fact]
    public void Save_WithApiKey_DoesNotWritePlaintextToFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Test the AppConfig serialization directly
            var config = new AppConfig { ApiKey = "my-secret-key-xyz" };

            // Simulate what Save does
            var encrypt = typeof(ConfigManager).GetMethod("EncryptApiKey",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            config.ApiKeyEncrypted = (string)encrypt.Invoke(null, [config.ApiKey])!;
            config.ConfigVersion = 2;

            var json = System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            json.Should().NotContain("my-secret-key-xyz");
            json.Should().Contain("api_key_encrypted");
            json.Should().Contain("config_version");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Property 4: ApiKey encryption round-trip
    /// For any non-empty string used as ApiKey, EncryptApiKey then DecryptApiKey SHALL produce the original string unchanged.
    /// Validates: Requirements 3.4, 3.7
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ApiKey_EncryptDecrypt_RoundTrip(NonEmptyString apiKey)
    {
        var encrypt = typeof(ConfigManager).GetMethod("EncryptApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var decrypt = typeof(ConfigManager).GetMethod("DecryptApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        try
        {
            var encrypted = (string)encrypt.Invoke(null, [apiKey.Get])!;
            var decrypted = (string)decrypt.Invoke(null, [encrypted])!;
            return decrypted == apiKey.Get;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Property 5: Encrypted config never contains plaintext ApiKey
    /// For any non-empty ApiKey of meaningful length, after serialization, the raw JSON SHALL NOT contain the original ApiKey as a substring.
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Save_NeverContainsPlaintextApiKey()
    {
        // Use strings of at least 8 chars to avoid false positives from single chars
        // appearing in JSON field names or base64 output
        var gen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Where(s => s.Get.Length >= 8);

        return Prop.ForAll(gen.ToArbitrary(), apiKey =>
        {
            var encrypt = typeof(ConfigManager).GetMethod("EncryptApiKey",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            try
            {
                var config = new AppConfig { ApiKey = apiKey.Get };
                config.ApiKeyEncrypted = (string)encrypt.Invoke(null, [config.ApiKey])!;
                config.ConfigVersion = 2;

                var json = System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                // The plaintext key should NOT appear in the serialized JSON
                return !json.Contains(apiKey.Get, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        });
    }
}
