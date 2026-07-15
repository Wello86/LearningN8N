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
/// T051 (quickstart.md "Multi-turn memory check", FR-012): proves a follow-up
/// message with no repeated order id resolves against the order mentioned in
/// an earlier turn of the same session, via the real
/// <see cref="ConversationHistoryLoader"/>/<see cref="ConversationRepository"/>
/// path — only <see cref="IChatModel"/> is scripted.
/// </summary>
public sealed class MultiTurnMemoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MultiTurnMemoryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatModel>();
                services.AddScoped<IChatModel, ScriptedHistoryAwareChatModel>();
            }));
    }

    [Fact]
    public async Task PostMessage_FollowUpWithoutOrderId_ResolvesAgainstOrderFromEarlierTurn()
    {
        var client = CreateClient("cust-1001");
        var sessionId = await CreateSessionAsync(client);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("Where is my order 12345?"));
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ChatTurnResponse>();
        firstBody!.ReferencedOrderIds.Should().Contain("12345");

        var followUpResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("and can I get a refund for that?"));

        followUpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var followUpBody = await followUpResponse.Content.ReadFromJsonAsync<ChatTurnResponse>();
        followUpBody!.EscalateToHuman.Should().BeFalse();
        followUpBody.ReferencedOrderIds.Should().Contain("12345");
        followUpBody.Message.Should().Contain("12345");
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
    /// Scripted <see cref="IChatModel"/> test double: extracts the order id
    /// from the current user message when present, or — mirroring how Claude
    /// would resolve a pronoun-only follow-up — from the most recent prior
    /// User-role message in the history block (research.md §5) when the
    /// current message has none, then calls <c>get_order_status</c>.
    /// </summary>
    private sealed class ScriptedHistoryAwareChatModel : IChatModel
    {
        private static readonly Regex OrderIdPattern = new(@"\d{4,}", RegexOptions.Compiled);

        public Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var toolResult = request.Messages.LastOrDefault(m => m.Role == ChatMessageRole.ToolResult);
            if (toolResult is not null)
            {
                return Task.FromResult(BuildFinalAnswer(toolResult));
            }

            var orderId = request.Messages
                .Where(m => m.Role == ChatMessageRole.User)
                .Reverse()
                .Select(m => OrderIdPattern.Match(m.Content))
                .FirstOrDefault(match => match.Success)
                ?.Value;

            var toolCall = new ToolCallRequest(Guid.NewGuid().ToString(), "get_order_status", $"{{\"orderId\":\"{orderId}\"}}");
            return Task.FromResult(new ChatCompletionResult(null, new[] { toolCall }));
        }

        private static ChatCompletionResult BuildFinalAnswer(ChatMessage toolResult)
        {
            if (!toolResult.Content.Contains("\"found\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatCompletionResult(GuardrailPolicy.LowConfidenceSentinel, Array.Empty<ToolCallRequest>());
            }

            return new ChatCompletionResult("Here is your order status. Details: " + toolResult.Content, Array.Empty<ToolCallRequest>());
        }
    }
}
