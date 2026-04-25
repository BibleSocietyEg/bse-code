using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using OpenAI.Chat;

namespace BSE_Code.Tests;

/// <summary>
/// Custom FsCheck generators for SlashCommandHandler property tests.
/// </summary>
public static class SlashCommandHandlerGenerators
{
    /// <summary>
    /// Generates a list of ChatMessages with arbitrary mixes of
    /// SystemChatMessage, UserChatMessage, and AssistantChatMessage.
    /// Always starts with a SystemChatMessage to match real REPL state.
    /// </summary>
    public static Arbitrary<List<ChatMessage>> MixedMessageLists()
    {
        var systemMsg = Gen.Constant((ChatMessage)new SystemChatMessage("You are a helpful assistant."));

        var userGen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => (ChatMessage)new UserChatMessage(s.Get));

        var assistantGen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => (ChatMessage)new AssistantChatMessage(s.Get));

        var nonSystemGen = Gen.OneOf(userGen, assistantGen);

        return Gen.Sized<List<ChatMessage>>(size =>
            nonSystemGen.ListOf(Math.Min(size, 10))
                .Select(rest =>
                {
                    var list = new List<ChatMessage> { new SystemChatMessage("You are a helpful assistant.") };
                    list.AddRange(rest);
                    return list;
                }))
            .ToArbitrary();
    }
}

[Collection("Sequential")]
public class SlashCommandHandlerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _originalDir = Directory.GetCurrentDirectory();

    public SlashCommandHandlerTests()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.SetCurrentDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDir);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Creates a SlashCommandHandler wired with stub delegates and an empty ToolRegistry.
    /// </summary>
    private static SlashCommandHandler MakeHandler(
        AppConfig? config = null,
        ChatClient? client = null,
        Func<ChatClient>? buildClient = null,
        Func<string>? buildSystemPrompt = null,
        Func<ChatClient, ChatCompletionOptions, List<ChatMessage>, string, Task>? runTurn = null)
    {
        config ??= new AppConfig { Model = "test-model", BaseUrl = "https://api.example.com/v1" };
        var registry = new ToolRegistry([]);

        // ChatClient requires a real endpoint; use a dummy one for tests that don't call the API.
        // We pass a stub buildClient so /model can swap it without hitting the network.
        var stubClient = client ?? new ChatClient(
            model: config.Model,
            credential: new System.ClientModel.ApiKeyCredential("test-key"),
            options: new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.example.com/v1") });

        buildClient ??= () => new ChatClient(
            model: config.Model,
            credential: new System.ClientModel.ApiKeyCredential("test-key"),
            options: new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.example.com/v1") });

        buildSystemPrompt ??= () => "You are a helpful assistant.";

        runTurn ??= (_, _, _, _) => Task.CompletedTask;

        return new SlashCommandHandler(
            config,
            registry,
            stubClient,
            buildClient,
            buildSystemPrompt,
            runTurn);
    }

    // ── /exit and /quit ───────────────────────────────────────────────────────

    [Fact]
    public async Task Exit_ReturnsOne()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>();
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/exit", messages, opts);

        result.Should().Be(1);
    }

    [Fact]
    public async Task Quit_ReturnsOne()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>();
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/quit", messages, opts);

        result.Should().Be(1);
    }

    // ── /clear ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clear_RemovesNonSystemMessages()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("Hello"),
            new AssistantChatMessage("Hi there"),
            new UserChatMessage("How are you?"),
            new AssistantChatMessage("I'm fine, thanks!")
        };
        var opts = new ChatCompletionOptions();

        await handler.HandleAsync("/clear", messages, opts);

        messages.Should().AllSatisfy(m => m.Should().BeOfType<SystemChatMessage>());
    }

    [Fact]
    public async Task Clear_PreservesSystemMessage()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("Hello"),
            new AssistantChatMessage("Hi there")
        };
        var opts = new ChatCompletionOptions();

        await handler.HandleAsync("/clear", messages, opts);

        messages.Should().HaveCount(1);
        messages[0].Should().BeOfType<SystemChatMessage>();
    }

    // ── /model ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Model_WithArg_UpdatesConfigAndRebuildsClient()
    {
        var config = new AppConfig { Model = "old-model", BaseUrl = "https://api.example.com/v1" };
        var buildClientCalled = false;
        var handler = MakeHandler(
            config: config,
            buildClient: () =>
            {
                buildClientCalled = true;
                return new ChatClient(
                    model: config.Model,
                    credential: new System.ClientModel.ApiKeyCredential("test-key"),
                    options: new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.example.com/v1") });
            });
        var messages = new List<ChatMessage>();
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/model new-model", messages, opts);

        result.Should().Be(0);
        config.Model.Should().Be("new-model");
        buildClientCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Model_NoArg_PrintsCurrentModel()
    {
        var config = new AppConfig { Model = "current-model", BaseUrl = "https://api.example.com/v1" };
        var buildClientCalled = false;
        var handler = MakeHandler(
            config: config,
            buildClient: () =>
            {
                buildClientCalled = true;
                return new ChatClient(
                    model: config.Model,
                    credential: new System.ClientModel.ApiKeyCredential("test-key"),
                    options: new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.example.com/v1") });
            });
        var messages = new List<ChatMessage>();
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/model", messages, opts);

        result.Should().Be(0);
        config.Model.Should().Be("current-model"); // unchanged
        buildClientCalled.Should().BeFalse();       // no rebuild
    }

    // ── /save ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_WithTag_CallsSessionManagerSave()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("Hello"),
            new AssistantChatMessage("Hi there")
        };
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/save my-session", messages, opts);

        result.Should().Be(0);

        // Verify the session was actually saved by trying to resume it
        var loaded = SessionManager.Resume("my-session", out var meta);
        loaded.Should().NotBeNull();
        meta!.Tag.Should().Be("my-session");
    }

    // ── /resume ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resume_WithTag_LoadsMessages()
    {
        // Pre-save a session so /resume can load it
        var initialMessages = new List<ChatMessage>
        {
            new UserChatMessage("What is 2+2?"),
            new AssistantChatMessage("4")
        };
        SessionManager.Save("test-resume", "test-model", initialMessages);

        var handler = MakeHandler();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant.")
        };
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/resume test-resume", messages, opts);

        result.Should().Be(0);
        // After resume: system message + loaded messages
        messages.Should().HaveCountGreaterThan(1);
        messages[0].Should().BeOfType<SystemChatMessage>();
        messages.Skip(1).Should().AllSatisfy(m =>
            (m is UserChatMessage || m is AssistantChatMessage).Should().BeTrue());
    }

    // ── /compact ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compact_FewerThanThreeUserMessages_ReturnsZeroWithoutCallingRunTurn()
    {
        var runTurnCalled = false;
        var handler = MakeHandler(
            runTurn: (_, _, _, _) =>
            {
                runTurnCalled = true;
                return Task.CompletedTask;
            });

        // Only 2 user messages — below the threshold of 3
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("Hello"),
            new AssistantChatMessage("Hi"),
            new UserChatMessage("How are you?"),
            new AssistantChatMessage("Fine, thanks!")
        };
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/compact", messages, opts);

        result.Should().Be(0);
        runTurnCalled.Should().BeFalse();
    }

    // ── Unknown command ───────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownCommand_NoSkill_ReturnsZero()
    {
        var handler = MakeHandler();
        var messages = new List<ChatMessage>();
        var opts = new ChatCompletionOptions();

        var result = await handler.HandleAsync("/nonexistent-command-xyz", messages, opts);

        result.Should().Be(0);
    }

    // ── Task 14.1: Property 4 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 4: /clear removes all non-system messages from any message list
    [Property(MaxTest = 100, Arbitrary = [typeof(SlashCommandHandlerGenerators)])]
    public bool Clear_RemovesAllNonSystemMessages(List<ChatMessage> messages)
    {
        var handler = MakeHandler();
        var opts = new ChatCompletionOptions();

        // Run /clear synchronously
        handler.HandleAsync("/clear", messages, opts).GetAwaiter().GetResult();

        // All remaining messages must be SystemChatMessage
        return messages.All(m => m is SystemChatMessage);
    }
}
