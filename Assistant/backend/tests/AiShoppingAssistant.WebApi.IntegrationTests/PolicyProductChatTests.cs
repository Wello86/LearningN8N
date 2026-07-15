using System.Net;
using System.Net.Http.Json;
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
/// T030: end-to-end coverage of spec Acceptance Scenarios for User Story 2
/// (policy/product search) against the real WebApi pipeline,
/// KnowledgeDocumentRepository's live pgvector cosine-similarity query, and
/// seeded Postgres data (docker/init/02-seed-sample-data.sql). Both
/// <see cref="IChatModel"/> and <see cref="IEmbeddingModel"/> are swapped for
/// scripted test doubles, since CodeMie's BaseUrl/ApiKey are empty in this
/// environment (appsettings.json) and no live LLM or embedding calls are
/// possible — everything else in the request path (routing, dev-auth,
/// search_policy_and_product_docs, the real pgvector query against the
/// seeded knowledge_documents table, GuardrailPolicy, conversation audit
/// logging) runs unmodified.
/// </summary>
public sealed class PolicyProductChatTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyProductChatTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatModel>();
                services.AddScoped<IChatModel, ScriptedPolicySearchChatModel>();
                services.RemoveAll<IEmbeddingModel>();
                services.AddScoped<IEmbeddingModel, FakeTopicEmbeddingModel>();
            }));
    }

    [Fact]
    public async Task PostMessage_ReturnsPolicyQuestion_AnswersFromPolicyTextWithoutEscalation()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("What is your return policy?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.Message.Should().ContainEquivalentOf("30 days");
    }

    [Fact]
    public async Task PostMessage_ProductQuestion_AnswersFromProductDescriptionWithoutEscalation()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Tell me about the wireless noise-cancelling headphones."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.Message.Should().ContainEquivalentOf("noise cancellation");
    }

    [Fact]
    public async Task PostMessage_UncoveredTopic_ReturnsFallbackAndEscalatesToHuman()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Do you offer free gift wrapping?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeTrue();
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
    /// Scripted <see cref="IChatModel"/> test double standing in for the real
    /// CodeMie-backed client. Mirrors just enough of Claude's native tool-use
    /// behavior to drive the real ReActLoop/search_policy_and_product_docs/
    /// KnowledgeDocumentRepository end-to-end: the first Reason step always
    /// requests search_policy_and_product_docs with the customer's own message
    /// as the query; once a ToolResult is present, it synthesizes a final
    /// answer from that typed result, or the guardrail sentinel when the tool
    /// reports nothing found.
    /// </summary>
    private sealed class ScriptedPolicySearchChatModel : IChatModel
    {
        public Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var toolResult = request.Messages.LastOrDefault(m => m.Role == ChatMessageRole.ToolResult);
            if (toolResult is not null)
            {
                return Task.FromResult(BuildFinalAnswer(toolResult));
            }

            var userMessage = request.Messages.Last(m => m.Role == ChatMessageRole.User).Content;
            var toolCall = new ToolCallRequest(
                Guid.NewGuid().ToString(),
                "search_policy_and_product_docs",
                System.Text.Json.JsonSerializer.Serialize(new { query = userMessage }));
            return Task.FromResult(new ChatCompletionResult(null, new[] { toolCall }));
        }

        private static ChatCompletionResult BuildFinalAnswer(ChatMessage toolResult)
        {
            if (!toolResult.Content.Contains("\"found\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatCompletionResult(GuardrailPolicy.LowConfidenceSentinel, Array.Empty<ToolCallRequest>());
            }

            return new ChatCompletionResult("Here's what I found: " + toolResult.Content, Array.Empty<ToolCallRequest>());
        }
    }

    /// <summary>
    /// Scripted <see cref="IEmbeddingModel"/> test double standing in for the
    /// real CodeMie-backed embedding client. For the exact queries this test
    /// class sends, returns the real embedding precomputed against the live
    /// CodeMie endpoint (<see cref="RealQueryEmbeddings"/>) — required since
    /// knowledge_documents (docker/init/02-seed-sample-data.sql) now carries
    /// real dense embeddings, not one-hot placeholders, so only a real query
    /// vector clears KnowledgeDocumentRepository's cosine-similarity
    /// threshold against them. An unrecognized query falls back to a one-hot
    /// vector in an unused dimension, orthogonal to every real seeded
    /// document — exercising the "nothing clears the threshold" guardrail
    /// path deterministically.
    /// </summary>
    private sealed class FakeTopicEmbeddingModel : IEmbeddingModel
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            if (RealQueryEmbeddings.TryGet(text, out var real))
            {
                return Task.FromResult(real);
            }

            var vector = new float[1536];
            vector[999] = 1f;
            return Task.FromResult(vector);
        }
    }
}
