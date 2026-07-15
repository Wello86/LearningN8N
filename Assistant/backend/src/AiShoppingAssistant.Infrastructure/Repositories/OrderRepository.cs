using AiShoppingAssistant.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace AiShoppingAssistant.Infrastructure.Repositories;

/// <summary>
/// EF Core/Npgsql implementation of <see cref="IOrderLookup"/>, backing the
/// <c>get_order_status</c> tool (contracts/react-tooling.md).
///
/// FR-010 (research.md §6): the ownership check is enforced by filtering on
/// BOTH <c>OrderId</c> and <c>CustomerId</c> in the SAME query, so an order
/// that exists but belongs to a different customer produces the exact same
/// "no row found" result as an order id that doesn't exist at all. The
/// caller (the <c>get_order_status</c> tool / the ReAct loop) never sees a
/// distinct "forbidden" outcome and therefore can never leak the existence
/// of another customer's order.
/// </summary>
public sealed class OrderRepository : IOrderLookup
{
    private readonly AiShoppingAssistantDbContext _dbContext;

    public OrderRepository(AiShoppingAssistantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderRecord?> FindOrderAsync(string orderId, string customerId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? null
            : new OrderRecord(order.OrderId, order.ProductName, order.OrderDate, order.DeliveryDate, order.Status, order.Amount);
    }
}
