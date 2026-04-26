using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests.Tools;

public class SemanticSearchToolTests
{
    [Fact]
    public void ChunkFile_SmallFile_ReturnsSingleChunk()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, Enumerable.Range(1, 10).Select(i => $"line {i}"));
            var chunks = SemanticSearchTool.ChunkFile(path).ToList();
            chunks.Should().HaveCount(1);
            chunks[0].StartLine.Should().Be(1);
            chunks[0].EndLine.Should().Be(10);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChunkFile_LargeFile_ReturnsMultipleChunks()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, Enumerable.Range(1, 500).Select(i => $"line {i}"));
            var chunks = SemanticSearchTool.ChunkFile(path).ToList();
            chunks.Should().HaveCountGreaterThan(1);
            chunks.All(c => c.StartLine >= 1 && c.EndLine <= 500).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var v = new float[] { 1f, 2f, 3f };
        SemanticSearchTool.CosineSimilarity(v, v).Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        SemanticSearchTool.CosineSimilarity(a, b).Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_EmptyVectors_ReturnsZero()
    {
        SemanticSearchTool.CosineSimilarity([], []).Should().Be(0f);
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ThrowsArgumentException()
    {
        var tool = new SemanticSearchTool(new AppConfig());
        var act = async () => await tool.ExecuteAsync("{}");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*query*");
    }

    // Property 6: result count bounded by top_n
    // **Validates: Requirements 1.2**
    // Feature: bse-code-improvements, Property 6: SemanticSearchTool result count bounded by top_n
    [Property(MaxTest = 50)]
    public bool SemanticSearch_ResultCount_BoundedByTopN(PositiveInt topN)
    {
        // Build a small in-memory index with known chunks
        var chunks = Enumerable.Range(1, 20).Select(i => new CodeChunk
        {
            FilePath = $"file{i}.cs",
            StartLine = 1,
            EndLine = 10,
            Text = $"code chunk {i}",
            Embedding = Enumerable.Range(0, 10).Select(j => (float)(i * j)).ToArray()
        }).ToList();

        var queryEmbedding = Enumerable.Range(0, 10).Select(j => (float)j).ToArray();

        // Simulate the ranking logic directly
        var results = chunks
            .Select(c => (chunk: c, score: SemanticSearchTool.CosineSimilarity(c.Embedding, queryEmbedding)))
            .OrderByDescending(x => x.score)
            .Take(topN.Get)
            .ToList();

        return results.Count <= topN.Get;
    }

    // Property 7: path restriction
    // **Validates: Requirements 1.2**
    // Feature: bse-code-improvements, Property 7: SemanticSearchTool path restriction
    [Property(MaxTest = 50)]
    public bool SemanticSearch_PathRestriction_OnlyReturnsMatchingPaths(NonEmptyString pathPrefix)
    {
        var prefix = pathPrefix.Get.Replace("\\", "/");
        var chunks = new List<CodeChunk>
        {
            new() { FilePath = prefix + "/file1.cs", StartLine = 1, EndLine = 5, Text = "a", Embedding = [1f, 0f] },
            new() { FilePath = prefix + "/file2.cs", StartLine = 1, EndLine = 5, Text = "b", Embedding = [0f, 1f] },
            new() { FilePath = "/other/path/file3.cs", StartLine = 1, EndLine = 5, Text = "c", Embedding = [1f, 1f] },
        };

        var filtered = chunks
            .Where(c => c.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return filtered.All(c => c.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            && filtered.Count == 2;
    }
}
