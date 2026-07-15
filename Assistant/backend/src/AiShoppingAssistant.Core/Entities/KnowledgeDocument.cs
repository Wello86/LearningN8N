namespace AiShoppingAssistant.Core.Entities;

/// <summary>
/// Distinguishes returns/delivery/warranty policy documents from product
/// descriptions (data-model.md "KnowledgeDocument").
/// </summary>
public enum KnowledgeDocumentType
{
    Policy,
    Product,
}

/// <summary>
/// Unstructured policy/product content backing User Story 2, searched via
/// pgvector cosine-similarity through the <c>search_policy_and_product_docs</c>
/// tool (contracts/react-tooling.md). Embeddings are generated once at
/// seed time (research.md §3) — this feature has no write/re-embed path.
/// </summary>
public sealed class KnowledgeDocument
{
    /// <summary>
    /// Internal id — never exposed to the customer (constitution Principle VI;
    /// the only id customers may ever see is an Order ID).
    /// </summary>
    public Guid DocumentId { get; set; }

    public KnowledgeDocumentType DocumentType { get; set; }

    public required string Title { get; set; }

    public required string Content { get; set; }

    /// <summary>
    /// 1536-dimension embedding of <see cref="Content"/> (research.md §3), as
    /// a plain float array so this Core entity carries no pgvector/EF Core
    /// package dependency (constitution Principle IV) — the Infrastructure
    /// mapping converts to/from <c>Pgvector.Vector</c> for the actual
    /// <c>vector(1536)</c> column. Required for a document to be eligible for
    /// retrieval (data-model.md validation note) — enforced at seed time, not
    /// query time.
    /// </summary>
    public required float[] Embedding { get; set; }
}
