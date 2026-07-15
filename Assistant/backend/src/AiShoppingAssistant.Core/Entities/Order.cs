namespace AiShoppingAssistant.Core.Entities;

/// <summary>
/// Lifecycle status of an <see cref="Order"/> (data-model.md "Order").
/// Drives the FR-001/User Story 1 answers — <see cref="Delayed"/> in
/// particular must be surfaced explicitly rather than described as a generic
/// "in progress" status (spec Acceptance Scenario 3).
/// </summary>
public enum OrderStatus
{
    InTransit,
    Delivered,
    Delayed,
    Returned,
}

/// <summary>
/// A customer's placed order (data-model.md "Order"). Structured record
/// queried via EF Core/Npgsql through the <c>get_order_status</c> tool
/// (contracts/react-tooling.md).
/// </summary>
public sealed class Order
{
    /// <summary>
    /// Customer-visible identifier (e.g. <c>12345</c>) — the one internal id
    /// customers may see per constitution Principle VI.
    /// </summary>
    public required string OrderId { get; set; }

    /// <summary>
    /// Owning customer id. Used for the FR-010 ownership check
    /// (<see cref="Ports.IOrderLookup"/> filters on this field in addition to
    /// <see cref="OrderId"/>). Never exposed in responses.
    /// </summary>
    public required string CustomerId { get; set; }

    public required string ProductName { get; set; }

    public DateOnly OrderDate { get; set; }

    /// <summary>Expected or actual delivery date, depending on <see cref="Status"/>.</summary>
    public DateOnly? DeliveryDate { get; set; }

    public OrderStatus Status { get; set; }

    public decimal Amount { get; set; }
}
