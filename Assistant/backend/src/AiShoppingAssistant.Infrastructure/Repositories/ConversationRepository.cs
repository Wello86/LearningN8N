using System.Text.Json;
using System.Text.Json.Serialization;
using AiShoppingAssistant.Core.Entities;
using AiShoppingAssistant.Core.Ports;
using AiShoppingAssistant.Core.ReAct;
using Microsoft.EntityFrameworkCore;

namespace AiShoppingAssistant.Infrastructure.Repositories;

/// <summary>
/// Persists <see cref="ConversationSession"/>/<see cref="ConversationTurn"/>
/// rows and reloads recent turns for the ReAct loop's conversation-history
/// block (FR-012). <see cref="AppendTurnAsync"/> is the audit-log write path
/// mandated by constitution Principle III (NON-NEGOTIABLE): it is the only
/// place a completed loop run is durably recorded, and it always writes the
/// raw user prompt, the tool calls executed, any similarity scores, the final
/// assistant response, and the <see cref="ConversationTurn.EscalateToHuman"/>
/// flag together, in one row, so the audit trail can never fall out of sync
/// with the response actually returned to the customer.
/// </summary>
public sealed class ConversationRepository : IConversationHistoryStore
{
    private static readonly JsonSerializerOptions AuditSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AiShoppingAssistantDbContext _dbContext;

    public ConversationRepository(AiShoppingAssistantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConversationSession> CreateSessionAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var session = new ConversationSession
        {
            SessionId = Guid.NewGuid(),
            CustomerId = customerId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.ConversationSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public Task<ConversationSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ConversationSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
    }

    /// <summary>All turns for a session, ordered by <see cref="ConversationTurn.TurnIndex"/> (GET /messages contract).</summary>
    public Task<List<ConversationTurn>> GetTurnsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ConversationTurns
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TurnIndex)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The last <paramref name="count"/> turns for a session, oldest first, for
    /// the ReAct loop's conversation-history block (FR-012 multi-turn memory,
    /// research.md §5's "small fixed window, e.g. 10").
    /// </summary>
    public async Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(Guid sessionId, int count, CancellationToken cancellationToken = default)
    {
        var turns = await _dbContext.ConversationTurns
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.TurnIndex)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        turns.Reverse();
        return turns;
    }

    /// <summary>
    /// Appends one completed Reason→Act→Observe loop run as a new, append-only
    /// audit-log row (constitution Principle III).
    /// </summary>
    public async Task<ConversationTurn> AppendTurnAsync(
        Guid sessionId,
        string userMessage,
        ReActLoopResult loopResult,
        CancellationToken cancellationToken = default)
    {
        var nextTurnIndex = await _dbContext.ConversationTurns
            .Where(t => t.SessionId == sessionId)
            .Select(t => (int?)t.TurnIndex)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        var turn = new ConversationTurn
        {
            TurnId = Guid.NewGuid(),
            SessionId = sessionId,
            TurnIndex = (nextTurnIndex ?? -1) + 1,
            UserMessage = userMessage,
            ToolCallsExecuted = SerializeToolCallsExecuted(loopResult.ToolCallsExecuted),
            RetrievedSimilarityScores = SerializeSimilarityScores(loopResult.ToolCallsExecuted),
            AssistantMessage = loopResult.AssistantMessage,
            EscalateToHuman = loopResult.EscalateToHuman,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.ConversationTurns.Add(turn);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return turn;
    }

    /// <summary>
    /// Re-derives the order ids referenced by a persisted turn's
    /// <see cref="ConversationTurn.ToolCallsExecuted"/> audit JSON (the
    /// inverse of <see cref="SerializeToolCallsExecuted"/>), for the
    /// <c>referencedOrderIds</c> field in the GET /messages history contract
    /// (contracts/chat-api.md). Always empty until an order-lookup tool is
    /// registered (Phase 3) — every audit entry has an <c>orderId</c>
    /// argument only for tool calls that carry one.
    /// </summary>
    public static IReadOnlyList<string> ExtractReferencedOrderIds(string toolCallsExecutedJson)
    {
        try
        {
            using var document = JsonDocument.Parse(toolCallsExecutedJson);
            var orderIds = new List<string>();

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("hasData", out var hasDataElement) || !hasDataElement.GetBoolean())
                {
                    continue;
                }

                if (entry.TryGetProperty("arguments", out var argumentsElement)
                    && argumentsElement.ValueKind == JsonValueKind.Object
                    && argumentsElement.TryGetProperty("orderId", out var orderIdElement)
                    && orderIdElement.ValueKind == JsonValueKind.String)
                {
                    var orderId = orderIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(orderId))
                    {
                        orderIds.Add(orderId);
                    }
                }
            }

            return orderIds;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string SerializeToolCallsExecuted(IReadOnlyList<ToolInvocationResult> toolCalls)
    {
        var entries = toolCalls.Select(call => new ToolCallAuditEntry(
            call.ToolName,
            ParseArguments(call.ArgumentsJson),
            call.HasData));

        return JsonSerializer.Serialize(entries, AuditSerializerOptions);
    }

    private static string? SerializeSimilarityScores(IReadOnlyList<ToolInvocationResult> toolCalls)
    {
        var scoredCalls = toolCalls
            .Where(call => call.SimilarityScores is { Count: > 0 })
            .Select(call => new SimilarityScoreAuditEntry(call.ToolName, call.SimilarityScores!))
            .ToList();

        return scoredCalls.Count == 0 ? null : JsonSerializer.Serialize(scoredCalls, AuditSerializerOptions);
    }

    private static JsonElement ParseArguments(string argumentsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        }
        catch (JsonException)
        {
            // Defensive: audit logging must never throw and drop the turn just
            // because a tool's raw arguments weren't valid JSON.
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(argumentsJson));
        }
    }

    private sealed record ToolCallAuditEntry(string ToolName, JsonElement Arguments, bool HasData);

    private sealed record SimilarityScoreAuditEntry(string ToolName, IReadOnlyList<double> Scores);
}
