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
/// T019: end-to-end coverage of spec Acceptance Scenarios 1-3 for User Story
/// 1 (order lookup) against the real WebApi pipeline, EF Core/Npgsql
/// repositories, and seeded Postgres data (docker/init/02-seed-sample-data.sql).
/// Only <see cref="IChatModel"/> is swapped for a scripted test double, since
/// CodeMie's BaseUrl/ApiKey are empty in this environment (appsettings.json)
/// and no live LLM calls are possible — everything else in the request path
/// (routing, dev-auth, get_order_status, OrderRepository, GuardrailPolicy,
/// conversation audit logging) runs unmodified.
/// </summary>
public sealed class OrderStatusChatTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderStatusChatTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatModel>();
                services.AddScoped<IChatModel, ScriptedOrderLookupChatModel>();
            }));
    }

    [Fact]
    public async Task PostMessage_ValidOrder_ReturnsStatusWithoutEscalation()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 12346?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.ReferencedOrderIds.Should().Contain("12346");
        body.Message.Should().Contain("12346");
    }

    [Fact]
    public async Task PostMessage_DelayedOrder_MentionsDelayExplicitlyWithoutEscalation()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 12345?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeFalse();
        body.Message.Should().ContainEquivalentOf("delayed");
        body.ReferencedOrderIds.Should().Contain("12345");
    }

    [Fact]
    public async Task PostMessage_UnknownOrder_ReturnsFallbackAndEscalatesToHuman()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 99999?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeTrue();
        body.ReferencedOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMessage_OrderBelongingToAnotherCustomer_ReturnsFallbackNeverLeaksOwnership()
    {
        // Order 12345 exists and belongs to cust-1001 (seed data) - requesting
        // it as cust-1002 must resolve identically to an unknown order id
        // (FR-010), never a distinguishable "forbidden" outcome.
        var client = CreateClient("cust-1002");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 12345?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        body!.EscalateToHuman.Should().BeTrue();
        body.ReferencedOrderIds.Should().BeEmpty();
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
    /// behavior to drive the real ReActLoop/get_order_status/OrderRepository
    /// end-to-end: the first Reason step always requests get_order_status for
    /// whatever order id appears in the user's message; once a ToolResult is
    /// present, it synthesizes a final answer from that typed result, or the
    /// guardrail sentinel when the tool reports not-found.
    /// </summary>
    private sealed class ScriptedOrderLookupChatModel : IChatModel
    {
        private static readonly Regex OrderIdPattern = new(@"\d{4,}", RegexOptions.Compiled);

        public Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var toolResult = request.Messages.LastOrDefault(m => m.Role == ChatMessageRole.ToolResult);
            if (toolResult is not null)
            {
                return Task.FromResult(BuildFinalAnswer(toolResult));
            }

            var userMessage = request.Messages.Last(m => m.Role == ChatMessageRole.User).Content;
            var orderId = OrderIdPattern.Match(userMessage).Value;
            var toolCall = new ToolCallRequest(Guid.NewGuid().ToString(), "get_order_status", $"{{\"orderId\":\"{orderId}\"}}");
            return Task.FromResult(new ChatCompletionResult(null, new[] { toolCall }));
        }

        private static ChatCompletionResult BuildFinalAnswer(ChatMessage toolResult)
        {
            if (!toolResult.Content.Contains("\"found\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatCompletionResult(GuardrailPolicy.LowConfidenceSentinel, Array.Empty<ToolCallRequest>());
            }

            var text = toolResult.Content.Contains("\"status\":\"Delayed\"", StringComparison.OrdinalIgnoreCase)
                ? "Your order is delayed. Details: " + toolResult.Content
                : "Here is your order status. Details: " + toolResult.Content;
            return new ChatCompletionResult(text, Array.Empty<ToolCallRequest>());
        }
    }
}
