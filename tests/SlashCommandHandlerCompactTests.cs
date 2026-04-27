using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using OpenAI.Chat;

namespace BSE_Code.Tests;

public class SlashCommandHandlerCompactTests
{
    private static List<ChatMessage> MakeMessages(int systemCount, int userCount, int assistantCount)
    {
        var msgs = new List<ChatMessage>();
        for (int i = 0; i < systemCount; i++)
            msgs.Add(new SystemChatMessage($"system {i}"));
        for (int i = 0; i < userCount; i++)
        {
            msgs.Add(new UserChatMessage($"user message {i} " + new string('x', 100)));
            if (i < assistantCount)
                msgs.Add(new AssistantChatMessage($"assistant reply {i} " + new string('y', 100)));
        }
        return msgs;
    }

    [Fact]
    public void EstimateTokens_EmptyList_ReturnsZero()
    {
        SlashCommandHandler.EstimateTokens([]).Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_NonEmptyMessages_ReturnsPositive()
    {
        var msgs = MakeMessages(1, 3, 3);
        SlashCommandHandler.EstimateTokens(msgs).Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateTokens_LargerMessages_ReturnsHigherCount()
    {
        var small = new List<ChatMessage> { new UserChatMessage("hi") };
        var large = new List<ChatMessage> { new UserChatMessage(new string('a', 400)) };
        SlashCommandHandler.EstimateTokens(large).Should().BeGreaterThan(
            SlashCommandHandler.EstimateTokens(small));
    }

    // Property 19: Token estimation is non-negative
    // Feature: bse-code-improvements, Property 19
    // Validates: Requirements 9.1
    [Property(MaxTest = 100)]
    public bool EstimateTokens_AlwaysNonNegative(NonNegativeInt systemCount, NonNegativeInt userCount)
    {
        var msgs = MakeMessages(
            Math.Min(systemCount.Get, 5),
            Math.Min(userCount.Get, 10),
            Math.Min(userCount.Get, 10));
        return SlashCommandHandler.EstimateTokens(msgs) >= 0;
    }

    // Property 20: Compaction pruning reduces token count below budget
    // Feature: bse-code-improvements, Property 20
    // Validates: Requirements 9.2
    [Fact]
    public void CompactionPruning_ReducesBelowBudget()
    {
        // Build a message list that exceeds the budget
        const int budget = 80_000;
        var msgs = new List<ChatMessage>();
        msgs.Add(new SystemChatMessage("system"));
        // Add many large messages to exceed budget
        for (int i = 0; i < 50; i++)
        {
            msgs.Add(new UserChatMessage(new string('u', 8000)));
            msgs.Add(new AssistantChatMessage(new string('a', 8000)));
        }

        var tokensBefore = SlashCommandHandler.EstimateTokens(msgs);
        tokensBefore.Should().BeGreaterThan(budget);

        var pruned = SlashCommandHandler.PruneToBudget(msgs, budget);
        SlashCommandHandler.EstimateTokens(pruned).Should().BeLessThanOrEqualTo(budget);
    }

    // Property 21: Compaction preserves system messages and last 4 pairs
    // Feature: bse-code-improvements, Property 21
    // Validates: Requirements 9.3
    [Fact]
    public void CompactionPruning_PreservesSystemAndLastFourPairs()
    {
        const int budget = 80_000;
        var msgs = new List<ChatMessage>();
        msgs.Add(new SystemChatMessage("keep-me"));
        for (int i = 0; i < 20; i++)
        {
            msgs.Add(new UserChatMessage(new string('u', 8000)));
            msgs.Add(new AssistantChatMessage(new string('a', 8000)));
        }

        var nonSystem = msgs.Where(m => m is not SystemChatMessage).ToList();
        var protectedMessages = nonSystem.TakeLast(8).ToList();

        var result = SlashCommandHandler.PruneToBudget(msgs, budget);

        // System messages preserved
        result.OfType<SystemChatMessage>().Should().HaveCount(1);
        result.OfType<SystemChatMessage>().First().Content[0].Text.Should().Be("keep-me");

        // Last 8 non-system messages (4 pairs) preserved
        var resultNonSystem = result.Where(m => m is not SystemChatMessage).ToList();
        resultNonSystem.TakeLast(8).Should().BeEquivalentTo(protectedMessages);
    }
}
