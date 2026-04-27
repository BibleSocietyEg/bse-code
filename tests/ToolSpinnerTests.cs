namespace BSE_Code.Tests;

/// <summary>
/// Tests for the ToolSpinner class introduced to show a visual loading indicator
/// during tool execution.
///
/// ToolSpinner is a lightweight inline spinner that:
///   - Starts animating immediately on construction
///   - Uses the theme's ToolColor (respects active color theme)
///   - Stops cleanly via Stop() or Dispose(), erasing the spinner frame
///   - Is safe to Stop() multiple times (idempotent)
///   - Works correctly in non-interactive environments (CI, redirected output)
/// </summary>
[Collection("Sequential")]
public class ToolSpinnerTests
{
    // ── Lifecycle tests ───────────────────────────────────────────────────────

    [Fact]
    public void ToolSpinner_CanBeCreatedAndDisposed_WithoutThrowing()
    {
        // Should not throw even in a non-interactive test environment
        var act = () =>
        {
            using var spinner = new ToolSpinner();
            // Immediately dispose — simulates a very fast tool
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ToolSpinner_Stop_IsIdempotent()
    {
        // Calling Stop() multiple times must not throw
        var act = () =>
        {
            var spinner = new ToolSpinner();
            spinner.Stop();
            spinner.Stop(); // second call — must be a no-op
            spinner.Stop(); // third call — still fine
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ToolSpinner_Dispose_AfterStop_DoesNotThrow()
    {
        var act = () =>
        {
            var spinner = new ToolSpinner();
            spinner.Stop();
            spinner.Dispose(); // Dispose after explicit Stop — must be safe
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ToolSpinner_UsingBlock_StopsCleanly()
    {
        // The using pattern is the primary intended usage
        var act = () =>
        {
            using var spinner = new ToolSpinner();
            // Simulate some work
            Thread.Sleep(50);
        }; // Dispose called here

        act.Should().NotThrow();
    }

    [Fact]
    public void ToolSpinner_StopBeforeFirstFrame_DoesNotThrow()
    {
        // Stop immediately — the background thread may not have written a frame yet
        var act = () =>
        {
            var spinner = new ToolSpinner();
            spinner.Stop(); // race: may stop before first 80ms tick
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ToolSpinner_MultipleSpinnersSequential_DoNotInterfere()
    {
        // Sequential spinners (as used per tool call) must each complete cleanly
        var act = () =>
        {
            for (int i = 0; i < 3; i++)
            {
                using var spinner = new ToolSpinner();
                Thread.Sleep(20);
            }
        };

        act.Should().NotThrow();
    }

    // ── Integration: spinner wraps a task ────────────────────────────────────

    [Fact]
    public async Task ToolSpinner_WrapsAsyncTask_CompletesSuccessfully()
    {
        string? result = null;

        using var spinner = new ToolSpinner();
        result = await Task.Run(async () =>
        {
            await Task.Delay(30);
            return "done";
        });
        spinner.Stop();

        result.Should().Be("done");
    }

    [Fact]
    public async Task ToolSpinner_WrapsFailingTask_StopsCleanlyOnException()
    {
        var act = async () =>
        {
            using var spinner = new ToolSpinner();
            try
            {
                await Task.Run(async () =>
                {
                    await Task.Delay(10);
                    throw new InvalidOperationException("tool failed");
                });
            }
            catch
            {
                spinner.Stop();
                throw;
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tool failed");
    }
}
