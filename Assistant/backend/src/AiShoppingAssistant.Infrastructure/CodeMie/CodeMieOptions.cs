namespace AiShoppingAssistant.Infrastructure.CodeMie;

/// <summary>
/// Configuration for <see cref="CodeMieChatClient"/>, bound from the
/// <c>CodeMie</c> configuration section (appsettings / environment / user
/// secrets). No live credentials exist in this repository — <see cref="ApiKey"/>
/// is left blank in committed configuration and must be supplied via user
/// secrets or environment variables in any environment that actually calls
/// the gateway.
/// </summary>
public sealed class CodeMieOptions
{
    public const string SectionName = "CodeMie";

    /// <summary>Base URL of EPAM's CodeMie gateway, e.g. <c>https://codemie.epam.com/code-assistant-api/v1</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key/bearer token for the CodeMie gateway. Never committed — supply via secrets/environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model identifier to request, e.g. <c>anthropic.claude-sonnet-5</c>.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Upper bound on tokens generated per Reason-step call.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Embedding model identifier used by <see cref="CodeMieEmbeddingClient"/>
    /// to produce the 1536-dimension vectors backing
    /// <see cref="AiShoppingAssistant.Core.Entities.KnowledgeDocument.Embedding"/>
    /// (research.md §3).
    /// </summary>
    public string EmbeddingModel { get; set; } = string.Empty;
}
