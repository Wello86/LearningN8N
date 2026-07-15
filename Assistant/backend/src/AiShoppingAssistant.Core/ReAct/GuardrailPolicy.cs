namespace AiShoppingAssistant.Core.ReAct;

/// <summary>
/// Implements constitution Principle VII (Absolute Hallucination Guardrail,
/// NON-NEGOTIABLE) and the "Loop termination contract" from
/// contracts/react-tooling.md: decides whether a candidate final answer from
/// Claude may be surfaced to the customer as-is, or must be replaced by the
/// standard safe fallback with a binding <c>EscalateToHuman: true</c> flag.
///
/// Wholesale-discard triggers (any one is sufficient to replace the answer
/// with <see cref="FallbackMessage"/>):
/// 1. The candidate answer contains the <see cref="LowConfidenceSentinel"/>
///    token (Claude's self-reported low-confidence signal).
/// 2. Every tool call executed this turn found no data
///    (<see cref="ToolInvocationResult.HasData"/> is false for all of
///    them) — an order that doesn't exist/belong to the customer, or a
///    policy/product search with no chunk above the similarity threshold,
///    and nothing else to salvage.
/// 3. The loop hit its iteration cap without producing a final answer.
/// 4. There is no candidate answer at all (empty/whitespace text).
///
/// Partial-confidence case (US3, T045): when at least one tool call found
/// data and at least one did not — e.g. a combined order+policy question
/// where the order resolved but no policy document matched — the candidate
/// answer is KEPT (Claude is instructed by <c>SystemPromptBuilder</c> to
/// state the resolved part and flag the rest) but <c>EscalateToHuman</c> is
/// still set so a human follows up on the unresolved part.
///
/// The sentinel token itself, and any other tool/wiring detail, MUST NOT leak
/// into the customer-visible message — on wholesale discard, this policy
/// always substitutes <see cref="FallbackMessage"/> rather than trying to
/// sanitize the raw text.
/// </summary>
public sealed class GuardrailPolicy
{
    /// <summary>
    /// Sentinel token the system prompt instructs Claude to emit instead of
    /// guessing when it cannot confidently answer from tool results
    /// (research.md §4, contracts/react-tooling.md).
    /// </summary>
    public const string LowConfidenceSentinel = "[LOW_CONFIDENCE]";

    /// <summary>
    /// Standard "can't confidently answer" fallback message (constitution
    /// Principle VII). Customer-facing tone rules (Principle VI) apply: no
    /// jargon, no internal ids.
    /// </summary>
    public const string FallbackMessage =
        "I'm sorry, I can't confidently find the information needed to answer that. I'll connect you with a support agent who can help.";

    /// <summary>
    /// Evaluates a completed Reason→Act→Observe loop run and produces the
    /// final, guardrail-checked result.
    /// </summary>
    /// <param name="candidateAnswer">
    /// Claude's plain-text response once it stopped requesting tool calls, or
    /// null if the loop exhausted its iteration cap without one.
    /// </param>
    /// <param name="toolCallsExecuted">Every tool call executed this loop run, in order.</param>
    /// <param name="iterationCapReached">
    /// True when the loop reached its 3-iteration cap without Claude
    /// returning a final answer — itself a guardrail trigger.
    /// </param>
    public ReActLoopResult Evaluate(
        string? candidateAnswer,
        IReadOnlyList<ToolInvocationResult> toolCallsExecuted,
        bool iterationCapReached)
    {
        if (ShouldDiscardAnswer(candidateAnswer, toolCallsExecuted, iterationCapReached))
        {
            return new ReActLoopResult(
                FallbackMessage,
                EscalateToHuman: true,
                toolCallsExecuted,
                ReferencedOrderIds: Array.Empty<string>());
        }

        return new ReActLoopResult(
            candidateAnswer!,
            EscalateToHuman: AnyToolCallFoundNoData(toolCallsExecuted),
            toolCallsExecuted,
            ReferencedOrderIds: Array.Empty<string>());
    }

    /// <summary>
    /// True only when there is nothing resolvable to keep: the answer must be
    /// replaced wholesale with <see cref="FallbackMessage"/>.
    /// </summary>
    private static bool ShouldDiscardAnswer(
        string? candidateAnswer,
        IReadOnlyList<ToolInvocationResult> toolCallsExecuted,
        bool iterationCapReached)
    {
        if (iterationCapReached)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidateAnswer))
        {
            return true;
        }

        if (candidateAnswer.Contains(LowConfidenceSentinel, StringComparison.Ordinal))
        {
            return true;
        }

        // All tool calls (if any were made) found nothing — no resolved part
        // to salvage. When at least one tool call found data, the candidate
        // answer is kept (partial-confidence case, US3/T045) even though
        // some other tool call may have found no data.
        return toolCallsExecuted.Count > 0 && toolCallsExecuted.All(toolCall => !toolCall.HasData);
    }

    private static bool AnyToolCallFoundNoData(IReadOnlyList<ToolInvocationResult> toolCallsExecuted) =>
        toolCallsExecuted.Any(toolCall => !toolCall.HasData);
}
