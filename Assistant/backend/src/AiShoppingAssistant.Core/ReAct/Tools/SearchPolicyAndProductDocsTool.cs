using System.Text.Json;
using System.Text.Json.Serialization;
using AiShoppingAssistant.Core.Ports;

namespace AiShoppingAssistant.Core.ReAct.Tools;

/// <summary>
/// The <c>search_policy_and_product_docs</c> tool (contracts/react-tooling.md),
/// wiring <see cref="IPolicySearch"/> into the ReAct loop's Act step.
/// Registered as an <see cref="IReActTool"/> via DI so <see cref="ReActLoop"/>
/// can invoke it without depending on Infrastructure (constitution Principle IV).
/// </summary>
public sealed class SearchPolicyAndProductDocsTool : IReActTool
{
    /// <summary>Tool name advertised to Claude, per contracts/react-tooling.md.</summary>
    public const string Name = "search_policy_and_product_docs";

    private const string InputSchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" }
          },
          "required": ["query"]
        }
        """;

    private const string NotFoundJson = "{\"found\":false}";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPolicySearch _policySearch;

    public SearchPolicyAndProductDocsTool(IPolicySearch policySearch)
    {
        _policySearch = policySearch;
    }

    public ToolDefinitionInfo Definition { get; } = new(
        Name,
        "Search store policy documents (returns, delivery/delay, warranty) and product descriptions for " +
        "content relevant to a customer question.",
        InputSchemaJson);

    /// <summary>
    /// Executes the search (Act step). An empty result (no chunk clears
    /// <c>IPolicySearch</c>'s fixed similarity threshold, research.md §4) sets
    /// <c>HasData: false</c>, which <see cref="GuardrailPolicy"/> already
    /// treats as a fallback trigger identically to an order-not-found result —
    /// no tool-specific guardrail logic is needed.
    /// </summary>
    public async Task<ToolInvocationResult> InvokeAsync(string argumentsJson, ReActContext context, CancellationToken cancellationToken)
    {
        var query = ExtractQuery(argumentsJson);
        if (string.IsNullOrWhiteSpace(query))
        {
            return NotFoundResult(argumentsJson);
        }

        var chunks = await _policySearch.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
        {
            return NotFoundResult(argumentsJson);
        }

        var payload = new PolicySearchToolResult(
            Found: true,
            Results: chunks.Select(c => new PolicySearchResultItem(c.Title, c.Content)).ToList());

        var similarityScores = chunks.Select(c => c.SimilarityScore).ToList();

        return new ToolInvocationResult(
            Name,
            argumentsJson,
            HasData: true,
            JsonSerializer.Serialize(payload, SerializerOptions),
            similarityScores);
    }

    private static string? ExtractQuery(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("query", out var queryElement)
                && queryElement.ValueKind == JsonValueKind.String)
            {
                return queryElement.GetString();
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
    /// Deliberately omits <c>DocumentId</c>/<c>DocumentType</c> — the model
    /// only ever needs the title/content text to compose an answer, and
    /// customers must never see an internal document id (constitution
    /// Principle VI).
    /// </summary>
    private sealed record PolicySearchToolResult(bool Found, List<PolicySearchResultItem> Results);

    private sealed record PolicySearchResultItem(string Title, string Content);
}
