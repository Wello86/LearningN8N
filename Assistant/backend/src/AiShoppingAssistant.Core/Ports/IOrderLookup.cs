using AiShoppingAssistant.Core.Entities;

namespace AiShoppingAssistant.Core.Ports;

/// <summary>
/// Port to the structured order data store, used by the <c>get_order_status</c>
/// tool (contracts/react-tooling.md). Kept provider-agnostic (no EF Core/Npgsql
/// reference) so <c>AiShoppingAssistant.Core</c> never depends on Infrastructure
/// (constitution Principle IV).
/// </summary>
public interface IOrderLookup
{
    /// <summary>
    /// Looks up an order by <paramref name="orderId"/>, filtering by
    /// <paramref name="customerId"/> in the SAME query (FR-010, research.md
    /// §6). Returns <c>null</c> both when the order id doesn't exist at all
    /// AND when it exists but belongs to a different customer — the caller
    /// MUST NOT be able to distinguish the two cases, so that an
    /// authorization mismatch never leaks the existence of another
    /// customer's order (not-found, never "forbidden").
    /// </summary>
    Task<OrderRecord?> FindOrderAsync(string orderId, string customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly-typed order data handed to the ReAct loop's Observe step
/// (constitution Principle VIII) — deliberately excludes <see cref="Order.CustomerId"/>
/// so no internal customer id can ever reach prompt construction via this path.
/// </summary>
/// <param name="OrderId">The one internal id customers may see (constitution Principle VI).</param>
public sealed record OrderRecord(
    string OrderId,
    string ProductName,
    DateOnly OrderDate,
    DateOnly? DeliveryDate,
    OrderStatus Status,
    decimal Amount);
