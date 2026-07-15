using AiShoppingAssistant.Core.Entities;

namespace AiShoppingAssistant.Core.Ports;

/// <summary>
/// Port to the policy/product knowledge store, used by the
/// <c>search_policy_and_product_docs</c> tool (contracts/react-tooling.md).
/// Kept provider-agnostic (no EF Core/Npgsql/pgvector reference) so
/// <c>AiShoppingAssistant.Core</c> never depends on Infrastructure
/// (constitution Principle IV).
/// </summary>
public interface IPolicySearch
{
    /// <summary>
    /// Embeds <paramref name="query"/> and returns every
    /// <see cref="KnowledgeDocument"/> chunk whose cosine similarity clears
    /// the fixed threshold (research.md §4), ranked best-first. An empty
    /// result means "no chunk clears the threshold" — the caller (the
    /// <c>search_policy_and_product_docs</c> tool) treats that identically to
    /// <see cref="IOrderLookup"/>'s not-found case: a guardrail trigger, never
    /// an invented answer.
    /// </summary>
    Task<IReadOnlyList<PolicyChunk>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly-typed search-result chunk handed to the ReAct loop's Observe step
/// (constitution Principle VIII).
/// </summary>
/// <param name="Title">e.g. "Returns Policy", "Wireless Headphones — Product Description".</param>
/// <param name="Content">The matched document's text content.</param>
/// <param name="SimilarityScore">
/// Cosine similarity (1 - cosine distance) to the query embedding, in [-1, 1]
/// — persisted as a Principle III audit field (data-model.md
/// "RetrievedSimilarityScores").
/// </param>
public sealed record PolicyChunk(string Title, string Content, double SimilarityScore);
