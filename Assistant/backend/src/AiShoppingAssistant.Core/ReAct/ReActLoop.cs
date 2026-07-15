using AiShoppingAssistant.Core.Ports;

namespace AiShoppingAssistant.Core.ReAct;

/// <summary>
/// Hand-written Reason→Act→Observe loop (research.md §1, contracts/react-tooling.md)
/// implementing constitution Principle I (Hybrid RAG Data Routing). No tools
/// are registered yet in this phase — <see cref="_tools"/> is empty until later
/// phases register <c>get_order_status</c> / <c>search_policy_and_product_docs</c>
/// implementations of <see cref="IReActTool"/> via DI.
///
/// Every Reason-step call to <see cref="IChatModel"/> assembles messages in
/// four structurally distinct sections (constitution Principle II):
/// 1. System prompt (persona/tone rules, tool definitions, guardrail-sentinel instruction).
/// 2. Conversation-history block (prior turns' user/assistant pairs only).
/// 3. The current turn's new user message.
/// 4. Retrieved-data block(s) (only present after an Act step this loop run;
///    appended as distinct <see cref="ChatMessageRole.ToolResult"/> messages,
///    never merged into the history text).
/// </summary>
public sealed class ReActLoop
{
    /// <summary>Hard cap on Reason→Act→Observe cycles (contracts/react-tooling.md "Loop termination contract").</summary>
    public const int MaxIterations = 3;

    private readonly IChatModel _chatModel;
    private readonly IReadOnlyList<IReActTool> _tools;
    private readonly GuardrailPolicy _guardrailPolicy;

    public ReActLoop(IChatModel chatModel, IEnumerable<IReActTool> tools, GuardrailPolicy guardrailPolicy)
    {
        _chatModel = chatModel;
        _tools = tools.ToList();
        _guardrailPolicy = guardrailPolicy;
    }

    /// <summary>Runs one full Reason→Act→Observe loop for a single customer turn.</summary>
    public async Task<ReActLoopResult> RunAsync(ReActLoopRequest request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SystemPromptBuilder.Build();
        var toolDefinitions = _tools.Select(t => new ToolDefinition(t.Definition.Name, t.Definition.Description, t.Definition.InputSchemaJson)).ToList();
        var toolCallsExecuted = new List<ToolInvocationResult>();

        // Sections 2 + 3: conversation-history block, then the current turn's new message.
        var messages = BuildHistoryMessages(request.History);
        messages.Add(new ChatMessage(ChatMessageRole.User, request.UserMessage));

        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var chatRequest = new ChatRequest(systemPrompt, messages, toolDefinitions);
            var completion = await _chatModel.SendAsync(chatRequest, cancellationToken).ConfigureAwait(false);

            if (completion.ToolCalls.Count == 0)
            {
                // Claude returned plain text with no tool call: candidate final answer.
                return _guardrailPolicy.Evaluate(completion.TextResponse, toolCallsExecuted, iterationCapReached: false);
            }

            // Echo Claude's own tool_use requests back into history first — the
            // Messages API rejects a tool_result with no matching tool_use in
            // the immediately preceding assistant turn.
            foreach (var toolCall in completion.ToolCalls)
            {
                messages.Add(new ChatMessage(ChatMessageRole.ToolUse, toolCall.ArgumentsJson, toolCall.ToolName, toolCall.ToolCallId));
            }

            // Act: execute exactly the tool(s) Claude asked for, never speculatively.
            foreach (var toolCall in completion.ToolCalls)
            {
                var invocationResult = await InvokeToolAsync(toolCall, request.Context, cancellationToken).ConfigureAwait(false);
                toolCallsExecuted.Add(invocationResult);

                // Observe: section 4, a distinct retrieved-data message — never
                // concatenated into the history block (constitution Principle II).
                messages.Add(new ChatMessage(ChatMessageRole.ToolResult, invocationResult.RetrievedDataJson, invocationResult.ToolName, toolCall.ToolCallId));
            }
        }

        // Iteration cap reached without a final answer: guardrail trigger.
        return _guardrailPolicy.Evaluate(candidateAnswer: null, toolCallsExecuted, iterationCapReached: true);
    }

    private async Task<ToolInvocationResult> InvokeToolAsync(ToolCallRequest toolCall, ReActContext context, CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(t => string.Equals(t.Definition.Name, toolCall.ToolName, StringComparison.Ordinal));
        if (tool is null)
        {
            return ToolInvocationResult.Unregistered(toolCall.ToolName, toolCall.ArgumentsJson);
        }

        return await tool.InvokeAsync(toolCall.ArgumentsJson, context, cancellationToken).ConfigureAwait(false);
    }

    private static List<ChatMessage> BuildHistoryMessages(IReadOnlyList<ConversationTurnSummary> history)
    {
        var messages = new List<ChatMessage>(history.Count * 2);
        foreach (var turn in history)
        {
            messages.Add(new ChatMessage(ChatMessageRole.User, turn.UserMessage));
            messages.Add(new ChatMessage(ChatMessageRole.Assistant, turn.AssistantMessage));
        }

        return messages;
    }

    /// <summary>
    /// Fixed persona/tone + guardrail-sentinel system prompt (contracts/react-tooling.md
    /// "Message structure", section 1). Order-lookup/policy-specific tone rules are
    /// layered on top of this base prompt by <c>SystemPromptBuilder</c> in later phases
    /// once tools exist to describe.
    /// </summary>
    private static string BuildSystemPromptOld()
    {
        return
            "You are a customer-facing shopping assistant for an online store. " +
            "Be conversational, empathetic, and clear (constitution Principle VI). " +
            "Never use technical jargon (SQL, vector scores, error messages) and never expose internal " +
            "database identifiers to the customer, with the sole exception of Order IDs, which customers " +
            "already know and use themselves. " +
            "Only answer using information returned by your tools or this conversation's history — never " +
            "invent order details or policy content from general knowledge. " +
            $"If you are not confident you can answer correctly from the information available, respond with " +
            $"exactly the token {GuardrailPolicy.LowConfidenceSentinel} and nothing else, instead of guessing.";
    }
}
