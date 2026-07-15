namespace AiShoppingAssistant.WebApi.Middleware;

/// <summary>
/// DEV-ONLY AUTHENTICATION STAND-IN (research.md §6). No real authentication
/// system exists yet for this POC; this middleware is a narrow, obvious
/// placeholder seam so <c>get_order_status</c>'s FR-010 ownership check has a
/// customer id to filter on. It reads the customer id from the
/// <c>X-Customer-Id</c> request header and stores it on
/// <see cref="HttpContext.Items"/> for controllers/services to read via
/// <see cref="DevCustomerIdHttpContextExtensions.GetCustomerId"/>.
///
/// THIS MUST BE REPLACED by the platform's real session/auth integration
/// before this feature moves beyond a POC.
/// </summary>
public sealed class DevCustomerIdMiddleware
{
    public const string HeaderName = "X-Customer-Id";
    internal const string ItemsKey = "DevCustomerId";

    private readonly RequestDelegate _next;

    public DevCustomerIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response
                .WriteAsJsonAsync(new { error = $"Missing required '{HeaderName}' header (dev-auth stand-in, research.md §6)." })
                .ConfigureAwait(false);
            return;
        }

        context.Items[ItemsKey] = values.ToString();
        await _next(context).ConfigureAwait(false);
    }
}

/// <summary>Convenience accessors for the dev-auth stand-in customer id.</summary>
public static class DevCustomerIdHttpContextExtensions
{
    /// <summary>
    /// Returns the customer id set by <see cref="DevCustomerIdMiddleware"/>, or
    /// null if the middleware hasn't run (e.g. this request path isn't behind it).
    /// </summary>
    public static string? GetCustomerId(this HttpContext context)
    {
        return context.Items.TryGetValue(DevCustomerIdMiddleware.ItemsKey, out var value) ? value as string : null;
    }
}

/// <summary>Registration helper for <see cref="DevCustomerIdMiddleware"/>.</summary>
public static class DevCustomerIdMiddlewareExtensions
{
    public static IApplicationBuilder UseDevCustomerId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DevCustomerIdMiddleware>();
    }
}
