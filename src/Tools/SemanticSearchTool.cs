using System.Text;
using OpenAI.Embeddings;

/// <summary>
/// Searches the codebase by semantic meaning using embeddings.
/// Builds an in-memory vector index on first invocation and caches it.
/// </summary>
public sealed class SemanticSearchTool : IToolHandler
{
    private readonly AppConfig _config;

    // In-memory index
    private static readonly List<CodeChunk> _index = [];
    private static readonly Dictionary<string, DateTime> _fileTimestamps = [];
    private static readonly SemaphoreSlim _indexLock = new(1, 1);
    private static string _indexFingerprint = "";

    public SemanticSearchTool(AppConfig config) => _config = config;

    public string Name => "semantic_search";
    public string Description => "Search the codebase by semantic meaning using embeddings";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "query" },
        properties = new
        {
            query = new { type = "string", description = "Natural language search query" },
            path = new { type = "string", description = "Restrict search to this path (optional)" },
            top_n = new { type = "integer", description = "Number of results to return (default: 10)" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("'query' is required.");

        var searchPath = args.GetValueOrDefault("path", Directory.GetCurrentDirectory());
        int topN = 10;
        if (args.TryGetValue("top_n", out var topNStr) && int.TryParse(topNStr, out var n) && n > 0)
            topN = n;

        // Build embedding client
        EmbeddingClient embeddingClient;
        try
        {
            var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(_config.BaseUrl) };
            var openAiClient = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(_config.ApiKey), options);
            embeddingClient = openAiClient.GetEmbeddingClient(_config.EmbeddingModel);
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to create embedding client: {ex.Message}";
        }

        // Build/refresh index
        try
        {
            var currentFingerprint = $"{_config.EmbeddingModel}|{_config.BaseUrl}";
            await BuildOrRefreshIndexAsync(searchPath, embeddingClient, currentFingerprint);
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to build search index: {ex.Message}";
        }

        // Filter by path
        // Always filter by searchPath with directory-boundary check
        var resolvedSearch = Path.GetFullPath(searchPath);
        var sep = Path.DirectorySeparatorChar.ToString();
        var candidates = _index
            .Where(c =>
            {
                var fullPath = Path.GetFullPath(c.FilePath);
                return fullPath.StartsWith(resolvedSearch + sep, StringComparison.OrdinalIgnoreCase)
                    || fullPath.Equals(resolvedSearch, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (candidates.Count == 0)
            return "No indexed files found in the specified path.";

        // Generate query embedding
        float[] queryEmbedding;
        try
        {
            var response = await embeddingClient.GenerateEmbeddingAsync(query);
            queryEmbedding = response.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to generate query embedding: {ex.Message}";
        }

        // Rank by cosine similarity
        var results = candidates
            .Select(c => (chunk: c, score: CosineSimilarity(c.Embedding, queryEmbedding)))
            .OrderByDescending(x => x.score)
            .Take(topN)
            .ToList();

        // Format results
        var sb = new StringBuilder();
        sb.AppendLine($"Semantic search results for: \"{query}\"");
        sb.AppendLine();
        for (int i = 0; i < results.Count; i++)
        {
            var (chunk, score) = results[i];
            sb.AppendLine($"{i + 1}. {chunk.FilePath} (lines {chunk.StartLine}-{chunk.EndLine}, score: {score:F3})");
            sb.AppendLine(chunk.Text.Length > 200 ? chunk.Text[..200] + "..." : chunk.Text);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task BuildOrRefreshIndexAsync(string rootPath, EmbeddingClient client, string fingerprint)
    {
        await _indexLock.WaitAsync();
        try
        {
            // Check if embedding provider/model changed
            if (_indexFingerprint != fingerprint)
            {
                // Invalidate entire cache
                _index.Clear();
                _fileTimestamps.Clear();
                _indexFingerprint = fingerprint;
            }

            var extensions = new[] { ".cs", ".ts", ".js", ".py", ".go", ".java", ".md", ".txt" };
            var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToList();

            // Evict chunks for files that no longer exist
            var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var deletedFiles = _fileTimestamps.Keys.Where(f => !existingFiles.Contains(f)).ToList();
            foreach (var deleted in deletedFiles)
            {
                _index.RemoveAll(c => c.FilePath == deleted);
                _fileTimestamps.Remove(deleted);
            }

            foreach (var file in files)
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (_fileTimestamps.TryGetValue(file, out var cached) && cached == lastWrite)
                    continue; // up to date

                // Remove old chunks for this file
                _index.RemoveAll(c => c.FilePath == file);

                var chunks = ChunkFile(file).ToList();
                if (chunks.Count == 0) continue;

                // Generate embeddings in batches to avoid provider limits
                const int maxInputsPerRequest = 100;
                const int maxTokensPerRequest = 8000;
                var texts = chunks.Select(c => c.Text).ToList();
                bool anyBatchFailed = false;

                for (int batchStart = 0; batchStart < texts.Count;)
                {
                    var batchSize = Math.Min(maxInputsPerRequest, texts.Count - batchStart);
                    var batch = texts.Skip(batchStart).Take(batchSize).ToList();

                    // Estimate tokens (rough: 4 chars per token)
                    var batchTokens = batch.Sum(t => t.Length / 4);
                    if (batchTokens > maxTokensPerRequest)
                    {
                        // Reduce batch size if too many tokens
                        batchSize = Math.Max(1, batchSize * maxTokensPerRequest / batchTokens);
                        batch = texts.Skip(batchStart).Take(batchSize).ToList();
                    }

                    try
                    {
                        var embeddings = await client.GenerateEmbeddingsAsync(batch);
                        for (int i = 0; i < batchSize; i++)
                        {
                            var chunkIndex = batchStart + i;
                            chunks[chunkIndex].Embedding = embeddings.Value[i].ToFloats().ToArray();
                            _index.Add(chunks[chunkIndex]);
                        }
                    }
                    catch (Exception ex)
                    {
                        UI.Warn($"⚠️  Failed to embed batch in '{file}': {ex.Message}");
                        anyBatchFailed = true;
                        // Continue with next batch instead of aborting
                    }

                    // Advance by the actual number of items processed
                    batchStart += batchSize;
                }

                // Only mark the file as fully indexed if all batches succeeded
                if (!anyBatchFailed)
                {
                    _fileTimestamps[file] = lastWrite;
                }
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    internal static IEnumerable<CodeChunk> ChunkFile(string filePath)
    {
        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { yield break; }

        const int chunkSize = 200;
        const int overlap = 20;

        for (int start = 0; start < lines.Length; start += chunkSize - overlap)
        {
            int end = Math.Min(start + chunkSize, lines.Length);
            var text = string.Join('\n', lines[start..end]);
            if (string.IsNullOrWhiteSpace(text)) continue;

            yield return new CodeChunk
            {
                FilePath = filePath,
                StartLine = start + 1,
                EndLine = end,
                Text = text
            };

            if (end >= lines.Length) break;
        }
    }

    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom == 0 ? 0f : dot / denom;
    }
}

/// <summary>Represents a chunk of code with its embedding vector.</summary>
public sealed class CodeChunk
{
    public string FilePath { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Text { get; init; } = "";
    public float[] Embedding { get; set; } = [];
}
