using AiShoppingAssistant.Core.Ports;

namespace AiShoppingAssistant.Core.ReAct;

/// <summary>
/// Loads the last <see cref="WindowSize"/> <c>ConversationTurn</c> rows of a
/// session into the small, fixed history window the ReAct loop's Reason step
/// reasons over (FR-012 multi-turn memory, research.md §5) — so a combined
/// US3 follow-up ("what about its warranty?") can resolve against an order
/// or topic mentioned earlier in the same session. Mapped to
/// <see cref="ConversationTurnSummary"/> here, at the Core boundary, so only
/// user/assistant text (never tool-call or similarity-score audit data)
/// crosses into the history message section (constitution Principle II).
/// </summary>
public sealed class ConversationHistoryLoader
{
    /// <summary>Fixed conversation-history window size (research.md §5's "small fixed window, e.g. 10").</summary>
    public const int WindowSize = 10;

    private readonly IConversationHistoryStore _historyStore;

    public ConversationHistoryLoader(IConversationHistoryStore historyStore)
    {
        _historyStore = historyStore;
    }

    public async Task<IReadOnlyList<ConversationTurnSummary>> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var turns = await _historyStore.GetRecentTurnsAsync(sessionId, WindowSize, cancellationToken).ConfigureAwait(false);
        return turns.Select(t => new ConversationTurnSummary(t.UserMessage, t.AssistantMessage)).ToList();
    }
}
