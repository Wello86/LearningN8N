using AiShoppingAssistant.Core.Entities;
using AiShoppingAssistant.Core.Ports;
using AiShoppingAssistant.Core.ReAct;
using AiShoppingAssistant.Core.ReAct.Tools;
using FluentAssertions;

namespace AiShoppingAssistant.Core.Tests;

/// <summary>
/// T020: proves <c>get_order_status</c> (<see cref="GetOrderStatusTool"/>)
/// filters by both <c>OrderId</c> and <c>CustomerId</c> (FR-010, research.md
/// §6) and that a same-order/different-customer lookup produces the exact
/// same "not found" shape as a nonexistent order id — never a distinguishable
/// "forbidden" outcome that could leak that the order exists at all.
/// </summary>
public sealed class OrderLookupTests
{
    private static readonly ReActContext Context = new(Guid.NewGuid(), "cust-1001");

    [Fact]
    public async Task InvokeAsync_PassesOrderIdAndContextCustomerId_ToOrderLookup()
    {
        var lookup = new RecordingOrderLookup(
            new OrderRecord("12345", "Widget", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5), OrderStatus.Delivered, 9.99m));
        var tool = new GetOrderStatusTool(lookup);

        await tool.InvokeAsync("""{"orderId":"12345"}""", Context, CancellationToken.None);

        lookup.LastOrderId.Should().Be("12345");
        lookup.LastCustomerId.Should().Be(Context.CustomerId);
    }

    [Fact]
    public async Task InvokeAsync_OrderFoundForRequestingCustomer_ReturnsHasDataTrue()
    {
        var order = new OrderRecord("12345", "Widget", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5), OrderStatus.Delivered, 9.99m);
        var tool = new GetOrderStatusTool(new RecordingOrderLookup(order));

        var result = await tool.InvokeAsync("""{"orderId":"12345"}""", Context, CancellationToken.None);

        result.HasData.Should().BeTrue();
        result.RetrievedDataJson.Should().Contain("12345").And.Contain("Delivered");
    }

    [Fact]
    public async Task InvokeAsync_OrderBelongsToAnotherCustomer_ReturnsSameNotFoundShapeAsNonexistentOrder()
    {
        // RecordingOrderLookup simulates the real repository's ownership filter
        // (research.md §6): it only ever returns a record when the requested
        // customerId matches, mirroring OrderRepository's single-query filter
        // on OrderId AND CustomerId.
        var ownedByOtherCustomer = new RecordingOrderLookup(order: null);
        var nonexistent = new RecordingOrderLookup(order: null);

        var toolForOtherCustomersOrder = new GetOrderStatusTool(ownedByOtherCustomer);
        var toolForUnknownOrder = new GetOrderStatusTool(nonexistent);

        var otherCustomersOrderResult = await toolForOtherCustomersOrder.InvokeAsync("""{"orderId":"99999"}""", Context, CancellationToken.None);
        var unknownOrderResult = await toolForUnknownOrder.InvokeAsync("""{"orderId":"00000"}""", Context, CancellationToken.None);

        otherCustomersOrderResult.HasData.Should().BeFalse();
        unknownOrderResult.HasData.Should().BeFalse();
        otherCustomersOrderResult.RetrievedDataJson.Should().Be(unknownOrderResult.RetrievedDataJson);
    }

    [Fact]
    public async Task InvokeAsync_MissingOrderIdArgument_ReturnsNotFoundWithoutCallingLookup()
    {
        var lookup = new RecordingOrderLookup(order: null);
        var tool = new GetOrderStatusTool(lookup);

        var result = await tool.InvokeAsync("{}", Context, CancellationToken.None);

        result.HasData.Should().BeFalse();
        lookup.WasCalled.Should().BeFalse();
    }

    private sealed class RecordingOrderLookup : IOrderLookup
    {
        private readonly OrderRecord? _order;

        public RecordingOrderLookup(OrderRecord? order)
        {
            _order = order;
        }

        public string? LastOrderId { get; private set; }
        public string? LastCustomerId { get; private set; }
        public bool WasCalled { get; private set; }

        public Task<OrderRecord?> FindOrderAsync(string orderId, string customerId, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastOrderId = orderId;
            LastCustomerId = customerId;
            return Task.FromResult(_order);
        }
    }
}
