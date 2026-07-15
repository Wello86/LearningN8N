namespace AiShoppingAssistant.Core.ReAct;

/// <summary>
/// Ambient context available to a tool when it executes (Act step), e.g. for
/// the FR-010 ownership filter (<c>get_order_status</c> filters by both order
/// id and <see cref="CustomerId"/>).
/// </summary>
/// <param name="SessionId">The active <c>ConversationSession</c>.</param>
/// <param name="CustomerId">The requesting customer id, from the dev-auth stand-in (research.md §6).</param>
public sealed record ReActContext(Guid SessionId, string CustomerId);

/// <summary>
/// A prior turn's user/assistant message pair, used only to build the
/// conversation-history message section (constitution Principle II) — never
/// carries tool-result payloads.
/// </summary>
public sealed record ConversationTurnSummary(string UserMessage, string AssistantMessage);

/// <summary>
/// The result of invoking one tool during the Act step, already mapped to a
/// typed shape by the tool implementation before being serialized into the
/// "retrieved data" message block per constitution Principle VIII (strong
/// typing before the LLM reasoning step).
/// </summary>
/// <param name="ToolName">Which tool was invoked.</param>
/// <param name="ArgumentsJson">Raw JSON arguments the model supplied for this call.</param>
/// <param name="HasData">
/// False when the underlying lookup found nothing (not-found order, no
/// similarity-eligible document chunk, etc.) — a hallucination-guardrail
/// trigger per constitution Principle VII / research.md §4.
/// </param>
/// <param name="RetrievedDataJson">
/// The typed result serialized for the model's "retrieved data" section.
/// </param>
/// <param name="SimilarityScores">
/// Vector similarity score(s) for any retrieved chunks, or null when this
/// tool call did not run a vector search (Principle III audit field).
/// </param>
public sealed record ToolInvocationResult(
    string ToolName,
    string ArgumentsJson,
    bool HasData,
    string RetrievedDataJson,
    IReadOnlyList<double>? SimilarityScores = null)
{
    /// <summary>Convenience factory for a tool call requested for a tool that isn't registered.</summary>
    public static ToolInvocationResult Unregistered(string toolName, string argumentsJson) =>
        new(toolName, argumentsJson, HasData: false, RetrievedDataJson: "{\"found\":false,\"reason\":\"tool_not_registered\"}");
}

/// <summary>
/// A tool the ReAct loop's Act step can invoke. No tools are registered in
/// Phase 2 — concrete implementations (<c>get_order_status</c>,
/// <c>search_policy_and_product_docs</c>) are added in later phases.
/// </summary>
public interface IReActTool
{
    /// <summary>The tool definition advertised to Claude every turn (contracts/react-tooling.md).</summary>
    ToolDefinitionInfo Definition { get; }

    /// <summary>Executes the tool (Act step) and maps its result to a typed <see cref="ToolInvocationResult"/>.</summary>
    Task<ToolInvocationResult> InvokeAsync(string argumentsJson, ReActContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Tool metadata used by <see cref="IReActTool"/>. Distinct from
/// <c>AiShoppingAssistant.Core.Ports.ToolDefinition</c> only to avoid a
/// circular file dependency at this layer; <see cref="ReActLoop"/> maps one
/// to the other when calling <c>IChatModel</c>.
/// </summary>
/// <param name="Name">Tool name, e.g. <c>get_order_status</c>.</param>
/// <param name="Description">Natural-language description shown to the model.</param>
/// <param name="InputSchemaJson">JSON Schema (as text) describing the tool's expected arguments.</param>
public sealed record ToolDefinitionInfo(string Name, string Description, string InputSchemaJson);

/// <summary>Input to one full Reason→Act→Observe loop run for a single customer turn.</summary>
/// <param name="Context">Session/customer context available to tools.</param>
/// <param name="History">
/// The last N turns' user/assistant message pairs (FR-012 multi-turn
/// memory), kept structurally separate from the current turn and from any
/// retrieved-data blocks (constitution Principle II).
/// </param>
/// <param name="UserMessage">The new customer message for this turn.</param>
public sealed record ReActLoopRequest(
    ReActContext Context,
    IReadOnlyList<ConversationTurnSummary> History,
    string UserMessage);

/// <summary>
/// Outcome of one full loop run, ready for audit-log persistence
/// (<c>ConversationRepository</c>) and for the chat API response
/// (contracts/chat-api.md).
/// </summary>
/// <param name="AssistantMessage">
/// The customer-facing response — either Claude's candidate final answer, or
/// the standard fallback message when the guardrail triggered.
/// </param>
/// <param name="EscalateToHuman">Binding guardrail flag per constitution Principle VII.</param>
/// <param name="ToolCallsExecuted">Every tool call executed this loop run, in order (Principle III audit field).</param>
/// <param name="ReferencedOrderIds">Order ids surfaced this turn (empty until order-lookup tooling is registered).</param>
public sealed record ReActLoopResult(
    string AssistantMessage,
    bool EscalateToHuman,
    IReadOnlyList<ToolInvocationResult> ToolCallsExecuted,
    IReadOnlyList<string> ReferencedOrderIds);
