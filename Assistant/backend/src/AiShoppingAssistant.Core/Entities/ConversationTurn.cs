namespace AiShoppingAssistant.Core.Entities;

/// <summary>
/// One request/response exchange within a <see cref="ConversationSession"/>.
/// Doubles as the audit-log record mandated by constitution Principle III
/// (Conversation Audit Logging, NON-NEGOTIABLE) and is the source of the
/// "last N turns" context window used for multi-turn memory (spec FR-012).
/// This entity is append-only: turns are never edited or deleted.
/// </summary>
public sealed class ConversationTurn
{
    public Guid TurnId { get; set; }

    /// <summary>Foreign key to the owning <see cref="ConversationSession"/>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Ordering within the session, starting at 0.</summary>
    public int TurnIndex { get; set; }

    /// <summary>Raw customer prompt (Principle III audit field: "the raw user prompt").</summary>
    public required string UserMessage { get; set; }

    /// <summary>
    /// JSON array describing which tool(s) ran this turn and with what
    /// parameters/results, e.g. <c>get_order_status(orderId=12345)</c>
    /// (Principle III audit field: "the structured queries executed,
    /// including parameters"). Stored as <c>jsonb</c>. Never null — an empty
    /// JSON array (<c>"[]"</c>) is used when no tool was called.
    /// </summary>
    public required string ToolCallsExecuted { get; set; }

    /// <summary>
    /// JSON array of vector similarity score(s) for any KnowledgeDocument
    /// chunks retrieved this turn (Principle III audit field: "the vector
    /// similarity scores of any retrieved chunks"). Stored as <c>jsonb</c>;
    /// null when no vector search ran this turn.
    /// </summary>
    public string? RetrievedSimilarityScores { get; set; }

    /// <summary>
    /// Final response text shown to the customer (Principle III audit field:
    /// "the final LLM response returned to the user").
    /// </summary>
    public required string AssistantMessage { get; set; }

    /// <summary>
    /// Binding hallucination-guardrail flag per constitution Principle VII /
    /// spec FR-011. Never inferred from parsing <see cref="AssistantMessage"/>.
    /// </summary>
    public bool EscalateToHuman { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
