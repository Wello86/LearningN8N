using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AiShoppingAssistant.Core.Ports;
using AiShoppingAssistant.Core.ReAct;
using AiShoppingAssistant.WebApi.Controllers;
using AiShoppingAssistant.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiShoppingAssistant.WebApi.IntegrationTests;

/// <summary>
/// T041-T043: end-to-end coverage of spec Acceptance Scenarios for User Story
/// 3 (combined order + policy questions) against the real WebApi pipeline, EF
/// Core/Npgsql repositories, and seeded Postgres data
/// (docker/init/02-seed-sample-data.sql). Only <see cref="IChatModel"/> and
/// <see cref="IEmbeddingModel"/> are swapped for scripted test doubles, since
/// CodeMie's BaseUrl/ApiKey are empty in this environment — everything else
/// (routing, dev-auth, both tools, OrderRepository, KnowledgeDocumentRepository,
/// GuardrailPolicy's partial-confidence logic, conversation audit logging)
/// runs unmodified.
/// </summary>
public sealed class CombinedChatTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CombinedChatTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatModel>();
                services.AddScoped<IChatModel, ScriptedCombinedChatModel>();
                services.RemoveAll<IEmbeddingModel>();
                services.AddScoped<IEmbeddingModel, FakeTopicEmbeddingModel>();
            }));
    }

    [Fact]
    public async Task PostMessage_DelayedOrderWithApplicableDelayPolicy_AnswersBothWithoutEscalation()
    {
        // Order 12345 (seed data) is Delayed, and the Delivery Delay Policy
        // document (topic dim 1, "delay") grants a shipping fee refund for
        // delays past 7 days — a combined question about both must resolve
        // confidently, connecting the order's real status to the policy.
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("My order 12345 seems delayed - am I eligible for a refund under the delay policy?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.Message.Should().ContainEquivalentOf("delayed");
        body.Message.Should().ContainEquivalentOf("shipping fee refund");
        body.ReferencedOrderIds.Should().Contain("12345");
    }

    [Fact]
    public async Task PostMessage_OnTimeOrderWithInapplicableDelayPolicy_AnswersBothWithoutEscalation()
    {
        // Order 12346 (seed data) is Delivered on time, so the same Delivery
        // Delay Policy document must be reported as NOT applicable, not
        // silently ignored or misapplied.
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Is my order 12346 eligible for a refund under the delay policy?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.Message.Should().ContainEquivalentOf("on time");
        body.Message.Should().ContainEquivalentOf("doesn't apply");
        body.ReferencedOrderIds.Should().Contain("12346");
    }

    [Fact]
    public async Task PostMessage_ResolvableOrderWithUncoveredTopic_KeepsOrderAnswerButEscalatesToHuman()
    {
        // Order 12347 (seed data) resolves fine, but "gift wrapping" matches no
        // policy/product document (embedding fake routes it to an orthogonal
        // dimension) - GuardrailPolicy's partial-confidence path (T045) must
        // keep the resolved order answer rather than discarding it wholesale,
        // while still escalating for the unresolved part.
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 12347, and do you offer free gift wrapping?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeTrue();
        body.Message.Should().Contain("12347");
        body.ReferencedOrderIds.Should().Contain("12347");
    }

    private HttpClient CreateClient(string customerId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevCustomerIdMiddleware.HeaderName, customerId);
        return client;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/chat/sessions", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
        return body!.SessionId;
    }

    /// <summary>
    /// Scripted <see cref="IEmbeddingModel"/> test double mirroring
    /// PolicyProductChatTests' fake: for the exact queries this test class
    /// sends, returns the real embedding precomputed against the live
    /// CodeMie endpoint (<see cref="RealQueryEmbeddings"/>) — required since
    /// knowledge_documents (docker/init/02-seed-sample-data.sql) now carries
    /// real dense embeddings, not one-hot placeholders. Unrecognized text
    /// falls back to a one-hot vector in an unused dimension, orthogonal to
    /// every real seeded document, exercising the guardrail deterministically
    /// without a live embedding call.
    /// </summary>
    private sealed class FakeTopicEmbeddingModel : IEmbeddingModel
    {
        private const int Dimensions = 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            if (RealQueryEmbeddings.TryGet(text, out var real))
            {
                return Task.FromResult(real);
            }

            var vector = new float[Dimensions];
            vector[999] = 1f;
            return Task.FromResult(vector);
        }
    }

    /// <summary>
    /// Scripted <see cref="IChatModel"/> test double for US3: the first Reason
    /// step always requests BOTH get_order_status and
    /// search_policy_and_product_docs in the same turn (mirroring Claude's
    /// native parallel tool-use behavior, per the system prompt's US3 tone
    /// rules from <c>SystemPromptBuilder</c>); once both ToolResults are
    /// present, it synthesizes one answer connecting the order's real status
    /// to the policy content, or the appropriate partial-confidence / fully
    /// low-confidence outcome per <see cref="GuardrailPolicy"/>.
    /// </summary>
    private sealed class ScriptedCombinedChatModel : IChatModel
    {
        private static readonly Regex OrderIdPattern = new(@"\d{4,}", RegexOptions.Compiled);

        public Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var toolResults = request.Messages.Where(m => m.Role == ChatMessageRole.ToolResult).ToList();
            if (toolResults.Count > 0)
            {
                return Task.FromResult(BuildFinalAnswer(toolResults));
            }

            var userMessage = request.Messages.Last(m => m.Role == ChatMessageRole.User).Content;
            var orderId = OrderIdPattern.Match(userMessage).Value;
            var toolCalls = new[]
            {
                new ToolCallRequest(Guid.NewGuid().ToString(), "get_order_status", $"{{\"orderId\":\"{orderId}\"}}"),
                new ToolCallRequest(Guid.NewGuid().ToString(), "search_policy_and_product_docs", $"{{\"query\":\"{userMessage}\"}}"),
            };
            return Task.FromResult(new ChatCompletionResult(null, toolCalls));
        }

        private static ChatCompletionResult BuildFinalAnswer(List<ChatMessage> toolResults)
        {
            var orderResult = toolResults.FirstOrDefault(m => m.ToolName == "get_order_status");
            var policyResult = toolResults.FirstOrDefault(m => m.ToolName == "search_policy_and_product_docs");

            var orderFound = orderResult is not null && orderResult.Content.Contains("\"found\":true", StringComparison.OrdinalIgnoreCase);
            var policyFound = policyResult is not null && policyResult.Content.Contains("\"found\":true", StringComparison.OrdinalIgnoreCase);

            if (!orderFound && !policyFound)
            {
                return new ChatCompletionResult(GuardrailPolicy.LowConfidenceSentinel, Array.Empty<ToolCallRequest>());
            }

            if (orderFound && !policyFound)
            {
                return new ChatCompletionResult(
                    "Here's your order info: " + orderResult!.Content +
                    " I don't have anything on file about the rest of your question, so I'm connecting you with " +
                    "a support agent for that part.",
                    Array.Empty<ToolCallRequest>());
            }

            if (!orderFound && policyFound)
            {
                return new ChatCompletionResult(
                    "I couldn't find that order, so I'm connecting you with a support agent about it. " +
                    "Policy info: " + policyResult!.Content,
                    Array.Empty<ToolCallRequest>());
            }

            var isDelayed = orderResult!.Content.Contains("\"status\":\"Delayed\"", StringComparison.OrdinalIgnoreCase);
            var isDelayPolicy = policyResult!.Content.Contains("Delivery Delay Policy", StringComparison.OrdinalIgnoreCase);

            var text = isDelayPolicy
                ? (isDelayed
                    ? "Your order is delayed, and since that's more than 7 days beyond the estimate, you're eligible " +
                      "for a shipping fee refund. Details: " + orderResult.Content + " " + policyResult.Content
                    : "Your order arrived on time, so the delivery-delay refund policy doesn't apply here. Details: " +
                      orderResult.Content + " " + policyResult.Content)
                : "Here's what I found: " + orderResult.Content + " " + policyResult.Content;

            return new ChatCompletionResult(text, Array.Empty<ToolCallRequest>());
        }
    }
}
