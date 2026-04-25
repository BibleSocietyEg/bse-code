using FluentAssertions;
using OpenAI.Chat;

namespace BSE_Code.Tests;

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
}
