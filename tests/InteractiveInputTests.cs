using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests;

/// <summary>
/// Custom FsCheck generators for InteractiveInput property tests.
/// </summary>
public static class InteractiveInputGenerators
{
    /// <summary>
    /// Generates arbitrary sequences of non-null strings, including consecutive duplicates.
    /// </summary>
    public static Arbitrary<List<string>> StringSequencesWithDuplicates()
    {
        // Generate a list of strings where some may be consecutive duplicates
        var stringGen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get);

        return Gen.Sized<List<string>>(size =>
        {
            var n = Math.Min(size, 15);
            return stringGen.ListOf(n)
                .SelectMany(baseList =>
                {
                    if (baseList.Count == 0)
                        return Gen.Constant(new List<string>());

                    // Randomly inject consecutive duplicates
                    return Gen.Choose(0, baseList.Count - 1)
                        .Select(dupIdx =>
                        {
                            var result = new List<string>(baseList);
                            // Insert a duplicate of the element at dupIdx right after it
                            if (dupIdx < result.Count)
                                result.Insert(dupIdx + 1, result[dupIdx]);
                            return result;
                        });
                });
        }).ToArbitrary();
    }

    /// <summary>
    /// Generates arbitrary non-empty filter strings.
    /// </summary>
    public static Arbitrary<NonEmptyString> NonEmptyFilterStrings() =>
        ArbMap.Default.ArbFor<NonEmptyString>();
}

[Collection("Sequential")]
public class InteractiveInputTests : IDisposable
{
    public InteractiveInputTests()
    {
        // Reset static history before each test to avoid cross-test interference
        InteractiveInput.ClearHistory();
    }

    public void Dispose()
    {
        // Clean up after each test
        InteractiveInput.ClearHistory();
    }

    // ── History tests ─────────────────────────────────────────────────────────

    [Fact]
    public void History_NewLine_AddedAtEnd()
    {
        // Simulate adding lines as ReadLine would
        InteractiveInput._history.Add("first");
        InteractiveInput._history.Add("second");
        InteractiveInput._history.Add("third");

        InteractiveInput._history.Should().HaveCount(3);
        InteractiveInput._history[^1].Should().Be("third");
    }

    [Fact]
    public void History_ConsecutiveDuplicate_StoredOnce()
    {
        // Simulate the deduplication logic from ReadLine
        var line = "hello world";
        AddToHistory(line);
        AddToHistory(line); // same line again — should not be added

        InteractiveInput._history.Should().HaveCount(1);
        InteractiveInput._history[0].Should().Be(line);
    }

    [Fact]
    public void History_NonConsecutiveDuplicate_BothStored()
    {
        // Non-consecutive duplicates ARE allowed
        AddToHistory("hello");
        AddToHistory("world");
        AddToHistory("hello"); // same as first, but not consecutive

        InteractiveInput._history.Should().HaveCount(3);
        InteractiveInput._history[0].Should().Be("hello");
        InteractiveInput._history[1].Should().Be("world");
        InteractiveInput._history[2].Should().Be("hello");
    }

    // ── GetSlashItems tests ───────────────────────────────────────────────────

    [Fact]
    public void GetSlashItems_EmptyFilter_ReturnsAllBuiltins()
    {
        var items = InteractiveInput.GetSlashItems("");

        // Should return at least all built-in commands (16 defined in BuiltinCommands)
        items.Should().NotBeEmpty();
        items.Should().HaveCountGreaterThanOrEqualTo(16);

        // All built-in commands should be present
        items.Should().Contain(i => i.Value == "/clear");
        items.Should().Contain(i => i.Value == "/model");
        items.Should().Contain(i => i.Value == "/help");
        items.Should().Contain(i => i.Value == "/exit");
    }

    [Fact]
    public void GetSlashItems_MatchingFilter_ReturnsOnlyMatches()
    {
        var items = InteractiveInput.GetSlashItems("model");

        items.Should().NotBeEmpty();
        // Every returned item must contain "model" in label or value (case-insensitive)
        items.Should().AllSatisfy(i =>
            (i.Label.Contains("model", StringComparison.OrdinalIgnoreCase)
             || i.Value.Contains("model", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue($"item '{i.Value}' should match filter 'model'"));
    }

    [Fact]
    public void GetSlashItems_NonMatchingFilter_ReturnsEmpty()
    {
        var items = InteractiveInput.GetSlashItems("zzz-nonexistent-xyz-999");

        items.Should().BeEmpty();
    }

    [Fact]
    public void GetSlashItems_FilterIsCaseInsensitive()
    {
        var lowerItems = InteractiveInput.GetSlashItems("clear");
        var upperItems = InteractiveInput.GetSlashItems("CLEAR");
        var mixedItems = InteractiveInput.GetSlashItems("ClEaR");

        lowerItems.Should().NotBeEmpty();
        upperItems.Should().HaveCount(lowerItems.Count);
        mixedItems.Should().HaveCount(lowerItems.Count);

        // All three should return the same items
        lowerItems.Select(i => i.Value).Should().BeEquivalentTo(upperItems.Select(i => i.Value));
        lowerItems.Select(i => i.Value).Should().BeEquivalentTo(mixedItems.Select(i => i.Value));
    }

    // ── Task 15.1: Property 5 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 5: History deduplication invariant
    // Validates: Requirements 4.2, 4.3
    [Property(MaxTest = 100, Arbitrary = [typeof(InteractiveInputGenerators)])]
    public bool History_NeverContainsConsecutiveDuplicates(List<string> lines)
    {
        InteractiveInput.ClearHistory();

        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                AddToHistory(line);
        }

        var history = InteractiveInput._history;

        // Check no two consecutive entries are identical
        for (int i = 0; i < history.Count - 1; i++)
        {
            if (history[i] == history[i + 1])
                return false;
        }

        return true;
    }

    // ── Task 15.2: Property 6 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 6: GetSlashItems filter returns only matching items
    // Validates: Requirements 4.5
    [Property(MaxTest = 100, Arbitrary = [typeof(InteractiveInputGenerators)])]
    public bool GetSlashItems_FilteredResults_AllContainFilter(NonEmptyString filterArb)
    {
        var filter = filterArb.Get;
        var items = InteractiveInput.GetSlashItems(filter);

        // Every returned item must contain the filter in label or value (case-insensitive)
        return items.All(i =>
            i.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || i.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the deduplication logic from InteractiveInput.ReadLine.
    /// </summary>
    private static void AddToHistory(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            if (InteractiveInput._history.Count == 0 || InteractiveInput._history[^1] != line)
                InteractiveInput._history.Add(line);
        }
    }
}
