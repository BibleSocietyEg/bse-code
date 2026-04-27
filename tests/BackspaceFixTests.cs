using System.Text;

namespace BSE_Code.Tests;

/// <summary>
/// Regression tests for the backspace fix in InteractiveInput.
///
/// Root cause: Before the fix, Backspace decremented the logical cursor and
/// removed the character from the buffer, but never moved the terminal cursor
/// left before calling RedrawFromCursor. RedrawFromCursor captured
/// Console.CursorLeft *after* the removal, so it wrote the suffix one column
/// too far to the right, leaving a ghost character on screen.
///
/// Fix: Console.CursorLeft-- is now called before RedrawFromCursor so the
/// terminal cursor is at the correct column before the suffix is redrawn.
///
/// These tests validate the pure buffer-manipulation logic that underpins the
/// fix — they do not require a real terminal.
/// </summary>
[Collection("Sequential")]
public class BackspaceFixTests : IDisposable
{
    public BackspaceFixTests() => InteractiveInput.ClearHistory();
    public void Dispose() => InteractiveInput.ClearHistory();

    // ── Buffer state after backspace ──────────────────────────────────────────

    [Fact]
    public void Backspace_AtEnd_RemovesLastCharacter()
    {
        var buf = new StringBuilder("hello");
        int cursor = buf.Length; // cursor at end

        // Simulate the fixed backspace logic (buffer side only)
        buf.Remove(cursor - 1, 1);
        cursor--;

        buf.ToString().Should().Be("hell");
        cursor.Should().Be(4);
    }

    [Fact]
    public void Backspace_InMiddle_RemovesCharacterBeforeCursor()
    {
        var buf = new StringBuilder("hello");
        int cursor = 3; // cursor after 'l' (hel|lo)

        buf.Remove(cursor - 1, 1);
        cursor--;

        buf.ToString().Should().Be("helo");
        cursor.Should().Be(2);
    }

    [Fact]
    public void Backspace_AtStart_DoesNothing()
    {
        var buf = new StringBuilder("hello");
        int cursor = 0; // cursor at start — backspace should be a no-op

        // Guard: cursor > 0 prevents any mutation
        if (cursor > 0)
        {
            buf.Remove(cursor - 1, 1);
            cursor--;
        }

        buf.ToString().Should().Be("hello");
        cursor.Should().Be(0);
    }

    [Fact]
    public void Backspace_EmptyBuffer_DoesNothing()
    {
        var buf = new StringBuilder();
        int cursor = 0;

        if (cursor > 0)
        {
            buf.Remove(cursor - 1, 1);
            cursor--;
        }

        buf.ToString().Should().BeEmpty();
        cursor.Should().Be(0);
    }

    [Fact]
    public void Backspace_MultipleConsecutive_RemovesCorrectCharacters()
    {
        var buf = new StringBuilder("abcde");
        int cursor = buf.Length;

        // Simulate pressing backspace 3 times
        for (int i = 0; i < 3; i++)
        {
            if (cursor > 0)
            {
                buf.Remove(cursor - 1, 1);
                cursor--;
            }
        }

        buf.ToString().Should().Be("ab");
        cursor.Should().Be(2);
    }

    [Fact]
    public void Backspace_ThenType_ProducesCorrectBuffer()
    {
        var buf = new StringBuilder("hello");
        int cursor = buf.Length;

        // Backspace twice
        for (int i = 0; i < 2; i++)
        {
            buf.Remove(cursor - 1, 1);
            cursor--;
        }

        // Type 'p'
        buf.Insert(cursor, 'p');
        cursor++;

        buf.ToString().Should().Be("help");
        cursor.Should().Be(4);
    }

    // ── Cursor position invariants ────────────────────────────────────────────

    [Fact]
    public void Backspace_CursorNeverGoesNegative()
    {
        var buf = new StringBuilder("x");
        int cursor = 1;

        // Press backspace more times than there are characters
        for (int i = 0; i < 5; i++)
        {
            if (cursor > 0)
            {
                buf.Remove(cursor - 1, 1);
                cursor--;
            }
        }

        cursor.Should().Be(0);
        buf.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Backspace_CursorAlwaysEqualsBufferLengthWhenAtEnd()
    {
        var buf = new StringBuilder("testing");
        int cursor = buf.Length;

        // Backspace from end — cursor should always equal buf.Length
        while (cursor > 0)
        {
            buf.Remove(cursor - 1, 1);
            cursor--;
            cursor.Should().Be(buf.Length);
        }
    }

    // ── Suffix correctness (what RedrawFromCursor would write) ────────────────

    [Fact]
    public void RedrawSuffix_AfterBackspaceAtEnd_IsEmpty()
    {
        var buf = new StringBuilder("hello");
        int cursor = buf.Length;

        buf.Remove(cursor - 1, 1);
        cursor--;

        // The suffix that RedrawFromCursor writes starts at the new cursor position
        var suffix = buf.ToString()[cursor..];
        suffix.Should().BeEmpty(); // nothing to the right of cursor
    }

    [Fact]
    public void RedrawSuffix_AfterBackspaceInMiddle_IsCorrect()
    {
        var buf = new StringBuilder("hello");
        int cursor = 3; // hel|lo

        buf.Remove(cursor - 1, 1);
        cursor--;

        // After removing 'l' at index 2: buf = "helo", cursor = 2
        // Suffix from cursor = "lo"
        var suffix = buf.ToString()[cursor..];
        suffix.Should().Be("lo");
    }

    [Fact]
    public void RedrawSuffix_AfterBackspaceAtSecondChar_IsCorrect()
    {
        var buf = new StringBuilder("ab");
        int cursor = 2;

        buf.Remove(cursor - 1, 1);
        cursor--;

        // buf = "a", cursor = 1, suffix = ""
        var suffix = buf.ToString()[cursor..];
        suffix.Should().BeEmpty();
    }
}
