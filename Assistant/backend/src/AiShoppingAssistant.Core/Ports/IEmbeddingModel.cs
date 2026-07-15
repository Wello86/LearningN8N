namespace AiShoppingAssistant.Core.Ports;

/// <summary>
/// Port to the embedding model used to turn a policy/product search query
/// into the 1536-dimension vector compared against
/// <see cref="Entities.KnowledgeDocument.Embedding"/> (research.md §3). Kept
/// provider-agnostic so <c>IPolicySearch</c> implementations in Infrastructure
/// don't hard-code a specific gateway, mirroring how <see cref="IChatModel"/>
/// decouples the ReAct loop from CodeMie.
/// </summary>
public interface IEmbeddingModel
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
