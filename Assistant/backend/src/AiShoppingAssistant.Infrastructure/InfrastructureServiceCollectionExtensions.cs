using AiShoppingAssistant.Core.Ports;
using AiShoppingAssistant.Infrastructure.CodeMie;
using AiShoppingAssistant.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiShoppingAssistant.Infrastructure;

/// <summary>
/// DI wiring for the Infrastructure layer (EF Core/Npgsql + pgvector
/// DbContext, the CodeMie chat client, and repositories), so WebApi's
/// <c>Program.cs</c> stays a thin composition root.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Default");

        services.AddDbContext<AiShoppingAssistantDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector()));

        services.Configure<CodeMieOptions>(configuration.GetSection(CodeMieOptions.SectionName));
        services.AddHttpClient<IChatModel, CodeMieChatClient>();
        services.AddHttpClient<IEmbeddingModel, CodeMieEmbeddingClient>();

        services.AddScoped<ConversationRepository>();
        services.AddScoped<IConversationHistoryStore>(sp => sp.GetRequiredService<ConversationRepository>());
        services.AddScoped<IOrderLookup, OrderRepository>();
        services.AddScoped<IPolicySearch, KnowledgeDocumentRepository>();

        return services;
    }
}
