using AiShoppingAssistant.Core.Entities;
using AiShoppingAssistant.Core.ReAct;
using AiShoppingAssistant.Infrastructure.Repositories;
using AiShoppingAssistant.WebApi.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace AiShoppingAssistant.WebApi.Controllers;

/// <summary>
/// Chat transport endpoints per contracts/chat-api.md. Requests carry the
/// dev-auth stand-in header (<see cref="DevCustomerIdMiddleware"/>,
/// research.md §6) until a real auth integration replaces it.
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ConversationRepository _conversationRepository;
    private readonly ReActLoop _reActLoop;
    private readonly ConversationHistoryLoader _historyLoader;

    public ChatController(ConversationRepository conversationRepository, ReActLoop reActLoop, ConversationHistoryLoader historyLoader)
    {
        _conversationRepository = conversationRepository;
        _reActLoop = reActLoop;
        _historyLoader = historyLoader;
    }

    /// <summary>POST /api/chat/sessions — starts a new conversation session.</summary>
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(CreateSessionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var customerId = HttpContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            // DevCustomerIdMiddleware already rejects requests missing the
            // header before the pipeline reaches here; this is a defensive
            // guard, not the primary enforcement point.
            return Unauthorized(new { error = $"Missing required '{DevCustomerIdMiddleware.HeaderName}' header." });
        }

        var session = await _conversationRepository.CreateSessionAsync(customerId, cancellationToken).ConfigureAwait(false);

        var response = new CreateSessionResponse(session.SessionId, session.CreatedAt);

        // Not CreatedAtAction(nameof(GetMessagesAsync), ...): MVC's endpoint
        // routing trims the "Async" suffix from action names by default, so
        // nameof() and the registered route name disagree and route-value
        // generation throws "No route matches the supplied values."
        return Created($"/api/chat/sessions/{session.SessionId}/messages", response);
    }

    /// <summary>
    /// GET /api/chat/sessions/{sessionId}/messages — returns the turn history
    /// for a session, ordered by <see cref="ConversationTurn.TurnIndex"/>, so
    /// the widget can restore/replay a conversation on reload.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatTurnHistoryItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessagesAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _conversationRepository.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return NotFound(new { error = $"No session found with id '{sessionId}'." });
        }

        var turns = await _conversationRepository.GetTurnsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var response = turns.Select(MapToHistoryItem).ToList();
        return Ok(response);
    }

    /// <summary>
    /// POST /api/chat/sessions/{sessionId}/messages — sends one customer
    /// message, drives one full Reason→Act→Observe loop (research.md §1),
    /// audit-logs the completed turn (constitution Principle III), and
    /// returns the synthesized (or guardrail-fallback) response.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(ChatTurnResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostMessageAsync(Guid sessionId, [FromBody] SendMessageRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "The 'message' field is required and cannot be empty." });
        }

        var session = await _conversationRepository.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return NotFound(new { error = $"No session found with id '{sessionId}'." });
        }

        var history = await _historyLoader.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);

        var loopRequest = new ReActLoopRequest(
            new ReActContext(sessionId, session.CustomerId),
            history,
            request.Message);

        var loopResult = await _reActLoop.RunAsync(loopRequest, cancellationToken).ConfigureAwait(false);

        var turn = await _conversationRepository
            .AppendTurnAsync(sessionId, request.Message, loopResult, cancellationToken)
            .ConfigureAwait(false);

        return Ok(MapToChatTurnResponse(turn));
    }

    private static ChatTurnResponse MapToChatTurnResponse(ConversationTurn turn)
    {
        return new ChatTurnResponse(
            turn.TurnId,
            turn.AssistantMessage,
            turn.EscalateToHuman,
            ConversationRepository.ExtractReferencedOrderIds(turn.ToolCallsExecuted));
    }

    private static ChatTurnHistoryItem MapToHistoryItem(ConversationTurn turn)
    {
        return new ChatTurnHistoryItem(
            turn.TurnId,
            turn.UserMessage,
            turn.AssistantMessage,
            turn.EscalateToHuman,
            ConversationRepository.ExtractReferencedOrderIds(turn.ToolCallsExecuted),
            turn.CreatedAt);
    }
}

/// <summary>Response body for POST /api/chat/sessions.</summary>
public sealed record CreateSessionResponse(Guid SessionId, DateTimeOffset CreatedAt);

/// <summary>Request body for POST /api/chat/sessions/{sessionId}/messages.</summary>
public sealed record SendMessageRequest(string? Message);

/// <summary>Response body for POST /api/chat/sessions/{sessionId}/messages.</summary>
public sealed record ChatTurnResponse(Guid TurnId, string Message, bool EscalateToHuman, IReadOnlyList<string> ReferencedOrderIds);

/// <summary>One item in the GET /api/chat/sessions/{sessionId}/messages history response.</summary>
public sealed record ChatTurnHistoryItem(
    Guid TurnId,
    string UserMessage,
    string Message,
    bool EscalateToHuman,
    IReadOnlyList<string> ReferencedOrderIds,
    DateTimeOffset CreatedAt);
