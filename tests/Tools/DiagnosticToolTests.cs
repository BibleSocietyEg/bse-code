using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests.Tools;

public class DiagnosticToolTests
{
    private readonly DiagnosticTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("{}");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*command*");
    }

    [Fact]
    public async Task ExecuteAsync_EchoCommand_ReturnsValidJson()
    {
        var command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
        var result = await _tool.ExecuteAsync($$$"""{"command": "{{{command}}}"}""");

        var parsed = JsonSerializer.Deserialize<DiagnosticResult>(result)!;
        parsed.Should().NotBeNull();
        parsed.ExitCode.Should().BeGreaterThanOrEqualTo(-1);
        parsed.Diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void ParseMsBuild_ValidLine_ReturnsDiagnostic()
    {
        var line = "src/Foo.cs(10,5): error CS0001: Some error message";
        var results = DiagnosticTool.ParseMsBuild(line);

        results.Should().HaveCount(1);
        results[0].File.Should().Contain("Foo.cs");
        results[0].Line.Should().Be(10);
        results[0].Column.Should().Be(5);
        results[0].Severity.Should().Be("error");
        results[0].Code.Should().Be("CS0001");
        results[0].Message.Should().Be("Some error message");
    }

    [Fact]
    public void ParseMsBuild_NoMatches_ReturnsEmpty()
    {
        var output = "Build succeeded.\n0 Error(s)\n0 Warning(s)";
        var results = DiagnosticTool.ParseMsBuild(output);
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseMsBuild_MultipleLines_ReturnsAll()
    {
        var output = """
            src/A.cs(1,1): error CS0001: Error one
            src/B.cs(2,3): warning CS0002: Warning two
            """;
        var results = DiagnosticTool.ParseMsBuild(output);
        results.Should().HaveCount(2);
        results[0].Severity.Should().Be("error");
        results[1].Severity.Should().Be("warning");
    }

    // Property 8: DiagnosticTool result is always valid JSON with required fields
    // Feature: bse-code-improvements, Property 8
    // Validates: Requirements 1.2
    [Property(MaxTest = 50)]
    public bool DiagnosticTool_AlwaysReturnsValidJson(NonEmptyString command)
    {
        var tool = new DiagnosticTool();
        try
        {
            var argsJson = JsonSerializer.Serialize(new { command = command.Get });
            var result = tool.ExecuteAsync(argsJson).GetAwaiter().GetResult();
            var parsed = JsonSerializer.Deserialize<DiagnosticResult>(result);
            return parsed is not null && parsed.ExitCode >= -1 && parsed.Diagnostics is not null;
        }
        catch (ArgumentException) { return true; } // missing required param is expected
        catch { return false; }
    }

    // Property 9: MSBuild diagnostic parsing round-trip
    // Feature: bse-code-improvements, Property 9
    // Validates: Requirements 1.2
    [Property(MaxTest = 100)]
    public bool DiagnosticTool_MsBuildParsing_RoundTrip(
        NonEmptyString file, PositiveInt line, PositiveInt col,
        NonEmptyString code, NonEmptyString message)
    {
        // Build a valid MSBuild diagnostic line
        // Sanitize inputs: remove parens, colons, newlines that would break the format
        // Sanitize inputs to produce valid MSBuild format tokens
        // File: remove parens, colons, newlines, and control characters
        var cleanFile = System.Text.RegularExpressions.Regex.Replace(
            file.Get.Replace("(", "").Replace(")", "").Replace(":", ""),
            @"[\x00-\x1F\x7F]", "");
        // MSBuild code must match \w+ (word chars only)
        var cleanCode = System.Text.RegularExpressions.Regex.Replace(code.Get, @"\W", "");
        // Message: replace newlines/carriage returns with spaces, then trim to match ParseMsBuild behavior
        var cleanMsg  = System.Text.RegularExpressions.Regex.Replace(message.Get, @"[\r\n]", " ").Trim();

        if (string.IsNullOrWhiteSpace(cleanFile) || string.IsNullOrWhiteSpace(cleanCode) || string.IsNullOrWhiteSpace(cleanMsg))
            return true; // skip degenerate inputs

        var msBuildLine = $"{cleanFile}({line.Get},{col.Get}): error {cleanCode}: {cleanMsg}";
        var results = DiagnosticTool.ParseMsBuild(msBuildLine);

        if (results.Count != 1) return false;
        var d = results[0];
        return d.File == cleanFile
            && d.Line == line.Get
            && d.Column == col.Get
            && d.Severity == "error"
            && d.Code == cleanCode
            && d.Message == cleanMsg;
    }
}
