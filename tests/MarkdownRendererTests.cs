using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void EscapeMarkup_BracketsAreEscaped()
    {
        MarkdownRenderer.EscapeMarkup("[hello]").Should().Be("[[hello]]");
    }

    [Fact]
    public void ProcessInlineMarkdown_Bold_ConvertsToSpectreMarkup()
    {
        var result = MarkdownRenderer.ProcessInlineMarkdown("Hello **world**!");
        result.Should().Contain("[bold]world[/]");
    }

    [Fact]
    public void ProcessInlineMarkdown_InlineCode_ConvertsToSpectreMarkup()
    {
        var result = MarkdownRenderer.ProcessInlineMarkdown("Use `dotnet build`");
        result.Should().Contain("[cyan]dotnet build[/]");
    }

    [Fact]
    public void ConvertMarkdownToSpectreMarkup_Header1_RendersWithBoldUnderline()
    {
        var result = MarkdownRenderer.ConvertMarkdownToSpectreMarkup("# Title");
        result.Should().Contain("[bold underline]Title[/]");
    }

    [Fact]
    public void ConvertMarkdownToSpectreMarkup_Header2_RendersWithBoldBlue()
    {
        var result = MarkdownRenderer.ConvertMarkdownToSpectreMarkup("## Section");
        result.Should().Contain("[bold blue]Section[/]");
    }

    [Fact]
    public void ConvertMarkdownToSpectreMarkup_CodeBlock_RendersInCyan()
    {
        var markdown = "```csharp\nvar x = 1;\n```";
        var result = MarkdownRenderer.ConvertMarkdownToSpectreMarkup(markdown);
        result.Should().Contain("[cyan]var x = 1;[/]");
    }

    [Fact]
    public void Render_DoesNotThrow()
    {
        var act = () => MarkdownRenderer.Render("# Hello **world**");
        act.Should().NotThrow();
    }

    // Property 10: MarkdownRenderer plain-text fallback does not introduce ANSI sequences
    // Feature: bse-code-improvements, Property 10
    // Validates: Requirements 6.5
    // The property checks that Render() does not ADD ANSI sequences.
    // Inputs that already contain \x1b[ are excluded — we only test clean markdown text.
    [Property(MaxTest = 100)]
    public Property MarkdownRenderer_PlainText_NoAnsiSequences()
    {
        // Only generate strings that don't already contain ANSI escape sequences
        var gen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Where(s => !s.Get.Contains("\x1b["));

        return Prop.ForAll(gen.ToArbitrary(), markdown =>
        {
            // In test context, Console.IsOutputRedirected is true, so IsPlainText = true
            var originalOut = Console.Out;
            var capture = new System.IO.StringWriter();
            Console.SetOut(capture);
            try
            {
                MarkdownRenderer.Render(markdown.Get);
                var output = capture.ToString();
                // Render() must not introduce ANSI escape sequences into clean input
                return !output.Contains("\x1b[");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        });
    }
}
