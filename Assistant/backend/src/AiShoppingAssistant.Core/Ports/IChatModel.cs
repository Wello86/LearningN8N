namespace AiShoppingAssistant.Core.Ports;

/// <summary>
/// Port to the LLM reasoning/orchestration model (Claude Sonnet 5, reached
/// through EPAM's CodeMie gateway per research.md §2). Kept provider-agnostic
/// so the hand-written ReAct loop in <c>AiShoppingAssistant.Core.ReAct</c>
/// never depends on Infrastructure (constitution Principle IV).
/// </summary>
public interface IChatModel
{
    /// <summary>
    /// Sends one Reason-step request (system prompt, message history/current
    /// turn/retrieved-data sections, and tool definitions) and returns either
    /// a candidate final answer or one or more requested tool calls.
    /// </summary>
    Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Role of a single message passed to the chat model.</summary>
public enum ChatMessageRole
{
    /// <summary>A prior or current customer message.</summary>
    User,

    /// <summary>A prior assistant (final-answer) message from conversation history.</summary>
    Assistant,

    /// <summary>
    /// A typed tool result fed back to the model as a distinct "retrieved
    /// data" section (constitution Principle II) — never merged into the
    /// conversation-history text.
    /// </summary>
    ToolResult,

    /// <summary>
    /// Echoes back one of Claude's own tool-use requests from the immediately
    /// preceding Reason step. Every <see cref="ToolResult"/> message must be
    /// preceded by the matching <c>ToolUse</c> message in the same request —
    /// the Messages API rejects a <c>tool_result</c> with no corresponding
    /// <c>tool_use</c> in the prior assistant turn.
    /// </summary>
    ToolUse,
}

/// <summary>One message section sent to the chat model.</summary>
/// <param name="Role">Which section this message belongs to.</param>
/// <param name="Content">
/// The message text. For <see cref="ChatMessageRole.ToolResult"/> this is the
/// serialized typed tool result (already mapped to a domain record before
/// serialization per constitution Principle VIII), never a raw/untyped blob.
/// </param>
/// For <see cref="ChatMessageRole.ToolUse"/> this is the tool call's raw
/// <see cref="ToolCallRequest.ArgumentsJson"/>, echoed back verbatim.
/// </param>
/// <param name="ToolName">The originating tool name, set only for <see cref="ChatMessageRole.ToolResult"/> and <see cref="ChatMessageRole.ToolUse"/> messages.</param>
/// <param name="ToolCallId">
/// The provider-assigned id of the originating <see cref="ToolCallRequest"/>,
/// set only for <see cref="ChatMessageRole.ToolResult"/> and <see cref="ChatMessageRole.ToolUse"/>
/// messages, so a provider client can correlate the result back to the tool-use request.
/// </param>
public sealed record ChatMessage(ChatMessageRole Role, string Content, string? ToolName = null, string? ToolCallId = null);

/// <summary>Declares one tool Claude may request via native tool-use, per contracts/react-tooling.md.</summary>
/// <param name="Name">Tool name, e.g. <c>get_order_status</c>.</param>
/// <param name="Description">Natural-language description shown to the model.</param>
/// <param name="InputSchemaJson">JSON Schema (as text) describing the tool's expected arguments.</param>
public sealed record ToolDefinition(string Name, string Description, string InputSchemaJson);

/// <summary>
/// The full Reason-step request: system prompt, the distinct message
/// sections that make up the conversation (history + current turn +
/// any retrieved-data blocks so far this loop), and the tools on offer.
/// </summary>
public sealed record ChatRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools);

/// <summary>One tool call Claude requested instead of returning a final answer.</summary>
/// <param name="ToolCallId">Provider-assigned id correlating this call to its eventual result.</param>
/// <param name="ToolName">Which tool Claude wants invoked.</param>
/// <param name="ArgumentsJson">Raw JSON arguments Claude supplied for the tool call.</param>
public sealed record ToolCallRequest(string ToolCallId, string ToolName, string ArgumentsJson);

/// <summary>
/// Result of one Reason-step call: either a candidate final answer
/// (<see cref="TextResponse"/> populated, <see cref="ToolCalls"/> empty) or
/// one/more requested tool calls (<see cref="ToolCalls"/> populated).
/// </summary>
public sealed record ChatCompletionResult(string? TextResponse, IReadOnlyList<ToolCallRequest> ToolCalls);
