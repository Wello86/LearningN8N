using AiShoppingAssistant.Core.ReAct;
using AiShoppingAssistant.Core.ReAct.Tools;
using AiShoppingAssistant.Infrastructure;
using AiShoppingAssistant.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Infrastructure: EF Core/Npgsql + pgvector DbContext, CodeMie chat client, repositories.
builder.Services.AddInfrastructure(builder.Configuration);

// Core: the hand-written ReAct loop and its hallucination guardrail
// (constitution Principles I/VII).
builder.Services.AddScoped<GuardrailPolicy>();
builder.Services.AddScoped<ReActLoop>();
builder.Services.AddScoped<ConversationHistoryLoader>();

// User Story 1 (order lookup) and User Story 2 (policy/product search) tools.
builder.Services.AddScoped<IReActTool, GetOrderStatusTool>();
builder.Services.AddScoped<IReActTool, SearchPolicyAndProductDocsTool>();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health");

// DEV-ONLY AUTHENTICATION STAND-IN (research.md §6) — reads X-Customer-Id.
// Must be replaced by the platform's real session/auth integration.
// /health is exempt: infra healthchecks (docker-compose, k8s probes) don't send app headers.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseDevCustomerId());

app.UseAuthorization();

app.MapControllers();

app.Run();
