using AiShoppingAssistant.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AiShoppingAssistant.Infrastructure.Repositories;

/// <summary>
/// EF Core/Npgsql + pgvector implementation of <see cref="IPolicySearch"/>,
/// backing the <c>search_policy_and_product_docs</c> tool
/// (contracts/react-tooling.md).
///
/// The guardrail trigger for "no chunk clears the threshold" (research.md
/// §4) is a fixed cosine-similarity cutoff rather than a learned/configurable
/// one, matching the "fixed threshold" language in tasks.md T037 — 0.75 is a
/// POC-reasonable cutoff for a small, hand-curated document set, not a value
/// tuned against real query traffic.
/// </summary>
public sealed class KnowledgeDocumentRepository : IPolicySearch
{
    private const double SimilarityThreshold = 0.75;
    private const int MaxResults = 3;

    private readonly AiShoppingAssistantDbContext _dbContext;
    private readonly IEmbeddingModel _embeddingModel;

    public KnowledgeDocumentRepository(AiShoppingAssistantDbContext dbContext, IEmbeddingModel embeddingModel)
    {
        _dbContext = dbContext;
        _embeddingModel = embeddingModel;
    }

    public async Task<IReadOnlyList<PolicyChunk>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddingModel.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var queryVector = new Vector(queryEmbedding);

        var chunks = await _dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Select(d => new
            {
                d.Title,
                d.Content,
                Distance = d.Embedding.CosineDistance(queryVector),
            })
            .OrderBy(d => d.Distance)
            .Take(MaxResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return chunks
            .Select(c => new PolicyChunk(c.Title, c.Content, 1 - c.Distance))
            .Where(c => c.SimilarityScore >= SimilarityThreshold)
            .ToList();
    }
}
