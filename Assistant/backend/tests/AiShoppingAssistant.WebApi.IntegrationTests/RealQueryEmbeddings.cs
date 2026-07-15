using System.Text.Json;

namespace AiShoppingAssistant.WebApi.IntegrationTests;

/// <summary>
/// Real 1536-dim `codemie-text-embedding-ada-002` embeddings for the exact
/// query strings used by <c>PolicyProductChatTests</c>/<c>CombinedChatTests</c>,
/// precomputed once via the live CodeMie embedding endpoint (see
/// TestData/query-embeddings.json) and loaded here so the fake
/// <see cref="AiShoppingAssistant.Core.Ports.IEmbeddingModel"/> test doubles can
/// return real vectors without a live call during test runs. This is required
/// because docker/init/02-seed-sample-data.sql's knowledge_documents now carry
/// real dense embeddings rather than one-hot placeholders — a fake embedding
/// scheme unrelated to real embedding space would never clear
/// KnowledgeDocumentRepository's cosine-similarity threshold.
/// </summary>
internal static class RealQueryEmbeddings
{
    private static readonly Dictionary<string, float[]> Vectors = Load();

    public static bool TryGet(string query, out float[] embedding) => Vectors.TryGetValue(query, out embedding!);

    private static Dictionary<string, float[]> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "query-embeddings.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, float[]>>(json)
            ?? throw new InvalidOperationException($"Failed to load query embeddings from {path}");
    }
}
