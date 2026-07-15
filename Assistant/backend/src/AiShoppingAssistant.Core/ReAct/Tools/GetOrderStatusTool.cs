using System.Text.Json;
using System.Text.Json.Serialization;
using AiShoppingAssistant.Core.Ports;

namespace AiShoppingAssistant.Core.ReAct.Tools;

/// <summary>
/// The <c>get_order_status</c> tool (contracts/react-tooling.md), wiring
/// <see cref="IOrderLookup"/> into the ReAct loop's Act step. Registered as
/// an <see cref="IReActTool"/> via DI so <see cref="ReActLoop"/> can invoke
/// it without depending on Infrastructure (constitution Principle IV).
/// </summary>
public sealed class GetOrderStatusTool : IReActTool
{
    /// <summary>Tool name advertised to Claude, per contracts/react-tooling.md.</summary>
    public const string Name = "get_order_status";

    private const string InputSchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "orderId": { "type": "string" }
          },
          "required": ["orderId"]
        }
        """;

    private const string NotFoundJson = "{\"found\":false}";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IOrderLookup _orderLookup;

    public GetOrderStatusTool(IOrderLookup orderLookup)
    {
        _orderLookup = orderLookup;
    }

    public ToolDefinitionInfo Definition { get; } = new(
        Name,
        "Look up the current status, dates, and amount for one of the requesting customer's own orders by " +
        "order id. Returns not-found if the order id doesn't exist or doesn't belong to this customer.",
        InputSchemaJson);

    /// <summary>
    /// Executes the lookup (Act step). FR-010 (research.md §6): the ownership
    /// filter (order id AND the requester's customer id, from
    /// <see cref="ReActContext.CustomerId"/>) is enforced entirely inside
    /// <see cref="IOrderLookup.FindOrderAsync"/> — this tool never receives,
    /// and therefore never leaks, whether an order id exists for a different
    /// customer; it only ever sees "found" or "not found".
    /// </summary>
    public async Task<ToolInvocationResult> InvokeAsync(string argumentsJson, ReActContext context, CancellationToken cancellationToken)
    {
        var orderId = ExtractOrderId(argumentsJson);
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return NotFoundResult(argumentsJson);
        }

        var order = await _orderLookup.FindOrderAsync(orderId, context.CustomerId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            // Not-found covers both "no such order id" and "belongs to a
            // different customer" (FR-010) — this is itself the constitution
            // Principle VII hallucination-guardrail trigger (HasData: false).
            return NotFoundResult(argumentsJson);
        }

        var payload = new OrderStatusToolResult(
            Found: true,
            OrderId: order.OrderId,
            ProductName: order.ProductName,
            OrderDate: order.OrderDate,
            DeliveryDate: order.DeliveryDate,
            Status: order.Status.ToString(),
            Amount: order.Amount);

        return new ToolInvocationResult(Name, argumentsJson, HasData: true, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? ExtractOrderId(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("orderId", out var orderIdElement)
                && orderIdElement.ValueKind == JsonValueKind.String)
            {
                return orderIdElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Malformed arguments resolve as not-found below, never as a thrown error.
        }

        return null;
    }

    private static ToolInvocationResult NotFoundResult(string argumentsJson) =>
        new(Name, argumentsJson, HasData: false, NotFoundJson);

    /// <summary>
    /// Typed retrieved-data payload (constitution Principle VIII) fed to the
    /// model's "retrieved data" message section (contracts/react-tooling.md).
    /// </summary>
    private sealed record OrderStatusToolResult(
        bool Found,
        string OrderId,
        string ProductName,
        DateOnly OrderDate,
        DateOnly? DeliveryDate,
        string Status,
        decimal Amount);
}
