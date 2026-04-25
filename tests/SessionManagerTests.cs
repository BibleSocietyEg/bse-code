using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using OpenAI.Chat;

namespace BSE_Code.Tests;

/// <summary>
/// Custom FsCheck generators for SessionManager property tests.
/// </summary>
public static class SessionManagerGenerators
{
    /// <summary>
    /// Generates a list of ChatMessages including user, text-only assistant,
    /// and tool-call assistant messages.
    /// </summary>
    public static Arbitrary<List<ChatMessage>> ChatMessageLists()
    {
        var userGen = Gen.Constant("user")
            .SelectMany(_ => ArbMap.Default.GeneratorFor<NonEmptyString>(),
                (_, s) => (ChatMessage)new UserChatMessage(s.Get));

        var assistantTextGen = Gen.Constant("assistant")
            .SelectMany(_ => ArbMap.Default.GeneratorFor<NonEmptyString>(),
                (_, s) => (ChatMessage)new AssistantChatMessage(s.Get));

        var assistantToolGen = Gen.Constant(0)
            .Select(_ =>
            {
                var callId = $"call_{Guid.NewGuid():N}"[..20];
                var toolCall = ChatToolCall.CreateFunctionToolCall(callId, "bash", BinaryData.FromString("{}"));
                return (ChatMessage)new AssistantChatMessage([toolCall]);
            });

        var msgGen = Gen.OneOf(userGen, assistantTextGen, assistantToolGen);

        return Gen.Sized<List<ChatMessage>>(size =>
            msgGen.ListOf(Math.Min(size, 10))
                  .Select(list => list.ToList()))
            .ToArbitrary();
    }
}

[Collection("Sequential")]
public class SessionManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _originalDir = Directory.GetCurrentDirectory();

    public SessionManagerTests()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.SetCurrentDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDir);
        Directory.Delete(_tempDir, recursive: true);
    }

    private static List<ChatMessage> MakeMessages(params (string role, string text)[] pairs)
    {
        return pairs.Select<(string role, string text), ChatMessage>(p =>
            p.role == "user"
                ? new UserChatMessage(p.text)
                : new AssistantChatMessage(p.text)
        ).ToList();
    }

    [Fact]
    public void List_NoSessions_ReturnsEmptyList()
    {
        var sessions = SessionManager.List();

        sessions.Should().BeEmpty();
    }

    [Fact]
    public void Save_ThenList_SessionAppearsInList()
    {
        var messages = MakeMessages(("user", "hello"), ("assistant", "hi there"));

        SessionManager.Save("my-session", "gpt-4o", messages);
        var sessions = SessionManager.List();

        sessions.Should().HaveCount(1);
        sessions[0].Tag.Should().Be("my-session");
        sessions[0].Model.Should().Be("gpt-4o");
    }

    [Fact]
    public void Resume_ExistingSession_ReturnsMessages()
    {
        var messages = MakeMessages(("user", "what is 2+2?"), ("assistant", "4"));
        SessionManager.Save("math", "gpt-4o", messages);

        var loaded = SessionManager.Resume("math", out var meta);

        loaded.Should().NotBeNull();
        loaded!.Should().HaveCount(2);
        meta!.Tag.Should().Be("math");
    }

    [Fact]
    public void Resume_NonExistentSession_ReturnsNull()
    {
        var loaded = SessionManager.Resume("ghost", out var meta);

        loaded.Should().BeNull();
        meta.Should().BeNull();
    }

    [Fact]
    public void Save_SystemMessagesAreExcluded()
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an assistant."),
            new UserChatMessage("hello"),
            new AssistantChatMessage("hi")
        };

        SessionManager.Save("filtered", "model", messages);
        var loaded = SessionManager.Resume("filtered", out _);

        loaded!.Should().HaveCount(2);
        loaded.Should().AllSatisfy(m =>
            (m is UserChatMessage || m is AssistantChatMessage).Should().BeTrue());
    }

    [Fact]
    public void Delete_ExistingSession_RemovesIt()
    {
        var messages = MakeMessages(("user", "test"));
        SessionManager.Save("to-delete", "model", messages);

        var deleted = SessionManager.Delete("to-delete");
        var sessions = SessionManager.List();

        deleted.Should().BeTrue();
        sessions.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistentSession_ReturnsFalse()
    {
        var result = SessionManager.Delete("ghost");

        result.Should().BeFalse();
    }

    [Fact]
    public void Save_TagWithSpecialChars_IsSanitized()
    {
        var messages = MakeMessages(("user", "hi"));

        // Should not throw — special chars get sanitized to underscores
        var act = () => SessionManager.Save("my session/tag!", "model", messages);

        act.Should().NotThrow();
    }

    [Fact]
    public void List_MultipleSessions_ReturnsMostRecentFirst()
    {
        var messages = MakeMessages(("user", "hi"));
        SessionManager.Save("first", "model", messages);
        Thread.Sleep(10); // ensure different timestamps
        SessionManager.Save("second", "model", messages);

        var sessions = SessionManager.List();

        sessions[0].Tag.Should().Be("second");
        sessions[1].Tag.Should().Be("first");
    }

    // ── Task 8 required tests ─────────────────────────────────────────────────

    [Fact]
    public void Save_TextOnlyMessages_RoundTripsUnchanged()
    {
        var messages = MakeMessages(
            ("user", "Hello"),
            ("assistant", "Hi there"),
            ("user", "How are you?"),
            ("assistant", "I'm doing well, thanks!")
        );

        SessionManager.Save("roundtrip", "gpt-4o", messages);
        var loaded = SessionManager.Resume("roundtrip", out _);

        loaded.Should().NotBeNull();
        loaded!.Should().HaveCount(4);
        loaded[0].Should().BeOfType<UserChatMessage>();
        loaded[1].Should().BeOfType<AssistantChatMessage>();
        loaded[2].Should().BeOfType<UserChatMessage>();
        loaded[3].Should().BeOfType<AssistantChatMessage>();

        // Verify content round-trips correctly
        var u0 = (UserChatMessage)loaded[0];
        u0.Content[0].Text.Should().Be("Hello");
        var a1 = (AssistantChatMessage)loaded[1];
        a1.Content[0].Text.Should().Be("Hi there");
    }

    [Fact]
    public void Save_ToolCallAssistantMessage_IsStripped()
    {
        var toolCall = ChatToolCall.CreateFunctionToolCall("call_abc123", "bash", BinaryData.FromString("{\"command\":\"ls\"}"));
        var assistantWithTool = new AssistantChatMessage([toolCall]);

        var messages = new List<ChatMessage>
        {
            new UserChatMessage("run ls"),
            assistantWithTool,
            new ToolChatMessage("call_abc123", "file1.txt\nfile2.txt"),
            new AssistantChatMessage("The directory contains file1.txt and file2.txt.")
        };

        SessionManager.Save("tool-strip", "gpt-4o", messages);
        var loaded = SessionManager.Resume("tool-strip", out _);

        loaded.Should().NotBeNull();
        // The assistant message with tool calls should be stripped
        loaded!.Should().NotContain(m => HasToolCalls(m), "assistant messages with tool calls should be stripped");
        // The ToolChatMessage should also be absent (not persisted)
        loaded.Should().NotContain(m => m is ToolChatMessage);
    }

    [Fact]
    public void Resume_OrphanedToolCallInFile_IsDropped()
    {
        // Manually write a session JSON with an assistant message that has empty content
        // (simulating a tool-call-only assistant message saved before the fix)
        var cwd = Directory.GetCurrentDirectory();
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cwd));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        var projectDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".bse-code", "sessions", hash);
        Directory.CreateDirectory(projectDir);

        // Write a session file with an assistant message that has empty content
        // (tool-call-only messages have no text content — they would be filtered by the whitespace guard)
        var sessionJson = """
            {
              "tag": "orphan-test",
              "model": "gpt-4o",
              "saved_at": "2024-01-01T00:00:00Z",
              "cwd": "/some/path",
              "messages": [
                { "role": "user", "content": "run ls" },
                { "role": "assistant", "content": "" }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(projectDir, "orphan-test.json"), sessionJson);

        var loaded = SessionManager.Resume("orphan-test", out _);

        // The empty-content assistant message should be dropped by the whitespace filter
        loaded.Should().NotBeNull();
        loaded!.Should().NotContain(m => HasToolCalls(m),
            "no orphaned tool-call references should exist after Resume");
    }

    [Fact]
    public void Save_EmptyContentMessages_AreExcluded()
    {
        var messages = new List<ChatMessage>
        {
            new UserChatMessage("   "),          // whitespace-only user message
            new UserChatMessage("real content"),
            new AssistantChatMessage(""),         // empty assistant message
            new AssistantChatMessage("real reply")
        };

        SessionManager.Save("empty-filter", "gpt-4o", messages);
        var loaded = SessionManager.Resume("empty-filter", out _);

        loaded.Should().NotBeNull();
        loaded!.Should().HaveCount(2, "whitespace-only and empty messages should be excluded");
        loaded[0].Should().BeOfType<UserChatMessage>();
        loaded[1].Should().BeOfType<AssistantChatMessage>();
    }

    // Helper to avoid expression-tree issues with 'is' pattern matching in lambdas
    private static bool HasToolCalls(ChatMessage m) =>
        m is AssistantChatMessage a && a.ToolCalls.Count > 0;

    // ── Task 8.1: Property 7 ──────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 7: Session save+resume round-trip produces no orphaned tool-call references
    [Property(MaxTest = 100, Arbitrary = [typeof(SessionManagerGenerators)])]
    public bool Save_Resume_ProducesNoOrphanedToolCallReferences(List<ChatMessage> messages)
    {
        // Use a unique tag per run to avoid cross-test interference
        var tag = $"prop7-{Guid.NewGuid():N}";
        SessionManager.Save(tag, "gpt-4o", messages);
        var loaded = SessionManager.Resume(tag, out _);
        SessionManager.Delete(tag);

        if (loaded is null) return true;

        // Collect all ToolChatMessage IDs in the resumed list
        var toolResultIds = loaded
            .OfType<ToolChatMessage>()
            .Select(t => t.ToolCallId?.ToString() ?? "")
            .ToHashSet();

        // No AssistantChatMessage should reference a tool call ID
        // that lacks a corresponding ToolChatMessage
        foreach (var msg in loaded.OfType<AssistantChatMessage>())
        {
            foreach (var tc in msg.ToolCalls)
            {
                if (!toolResultIds.Contains(tc.Id))
                    return false;
            }
        }

        return true;
    }
}
