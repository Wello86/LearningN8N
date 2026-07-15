namespace AiShoppingAssistant.Core.Entities;

/// <summary>
/// Represents one customer's chat session, created when the chat widget opens.
/// Persisted so the stateless WebAPI can reload conversation context on later
/// turns (data-model.md "ConversationSession").
/// </summary>
public sealed class ConversationSession
{
    public Guid SessionId { get; set; }

    /// <summary>
    /// Owning customer id, supplied by the dev-auth stand-in
    /// (research.md §6, <c>X-Customer-Id</c> header) until real auth exists.
    /// </summary>
    public required string CustomerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
