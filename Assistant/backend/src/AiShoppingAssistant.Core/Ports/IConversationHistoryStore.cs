using AiShoppingAssistant.Core.Entities;

namespace AiShoppingAssistant.Core.Ports;

/// <summary>
/// Port to the durable <see cref="ConversationTurn"/> audit log, used by
/// <c>ConversationHistoryLoader</c> (T046) to load the last N turns of a
/// session into the ReAct loop's conversation-history block (FR-012). Kept
/// provider-agnostic (no EF Core/Npgsql reference) so
/// <c>AiShoppingAssistant.Core</c> never depends on Infrastructure
/// (constitution Principle IV).
/// </summary>
public interface IConversationHistoryStore
{
    /// <summary>
    /// The last <paramref name="count"/> turns for a session, oldest first.
    /// </summary>
    Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(Guid sessionId, int count, CancellationToken cancellationToken = default);
}
