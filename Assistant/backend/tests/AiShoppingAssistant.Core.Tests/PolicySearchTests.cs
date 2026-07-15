using AiShoppingAssistant.Core.Ports;
using AiShoppingAssistant.Core.ReAct;
using AiShoppingAssistant.Core.ReAct.Tools;
using FluentAssertions;

namespace AiShoppingAssistant.Core.Tests;

/// <summary>
/// T031: proves <c>search_policy_and_product_docs</c>
/// (<see cref="SearchPolicyAndProductDocsTool"/>) surfaces
/// <see cref="IPolicySearch"/> results as typed retrieved data with
/// similarity scores attached (Principle III audit field), and that an
/// empty result (no chunk clears the fixed similarity threshold,
/// research.md §4) produces the same <c>HasData: false</c> guardrail-trigger
/// shape as <c>get_order_status</c>'s not-found case.
/// </summary>
public sealed class PolicySearchTests
{
    private static readonly ReActContext Context = new(Guid.NewGuid(), "cust-1001");

    [Fact]
    public async Task InvokeAsync_PassesQueryArgument_ToPolicySearch()
    {
        var search = new RecordingPolicySearch(chunks: []);
        var tool = new SearchPolicyAndProductDocsTool(search);

        await tool.InvokeAsync("""{"query":"what is your return policy"}""", Context, CancellationToken.None);

        search.LastQuery.Should().Be("what is your return policy");
    }

    [Fact]
    public async Task InvokeAsync_ChunksFound_ReturnsHasDataTrueWithTitlesContentAndSimilarityScores()
    {
        var chunks = new[]
        {
            new PolicyChunk("Returns and Refunds Policy", "Items may be returned within 30 days.", 0.91),
            new PolicyChunk("Warranty Coverage", "Most products carry a 1-year limited warranty.", 0.78),
        };
        var tool = new SearchPolicyAndProductDocsTool(new RecordingPolicySearch(chunks));

        var result = await tool.InvokeAsync("""{"query":"can I return this"}""", Context, CancellationToken.None);

        result.HasData.Should().BeTrue();
        result.RetrievedDataJson.Should().Contain("Returns and Refunds Policy").And.Contain("30 days");
        result.SimilarityScores.Should().BeEquivalentTo(new[] { 0.91, 0.78 });
    }

    [Fact]
    public async Task InvokeAsync_ChunksFound_NeverIncludesDocumentIdOrRawScoresInRetrievedDataJson()
    {
        var chunks = new[] { new PolicyChunk("Warranty Coverage", "Covers manufacturing defects.", 0.80) };
        var tool = new SearchPolicyAndProductDocsTool(new RecordingPolicySearch(chunks));

        var result = await tool.InvokeAsync("""{"query":"warranty"}""", Context, CancellationToken.None);

        // The model-facing payload must carry only title/content text
        // (constitution Principle VI/VIII) — the similarity score is audit
        // data returned out-of-band via SimilarityScores, never embedded in
        // the JSON the model reasons over.
        result.RetrievedDataJson.Should().NotContain("0.8").And.NotContain("DocumentId");
    }

    [Fact]
    public async Task InvokeAsync_NoChunkClearsThreshold_ReturnsSameNotFoundShapeAsMissingQuery()
    {
        var noResultsForQuery = new RecordingPolicySearch(chunks: []);
        var missingQuery = new RecordingPolicySearch(chunks: []);

        var toolForUncoveredTopic = new SearchPolicyAndProductDocsTool(noResultsForQuery);
        var toolForMissingArgument = new SearchPolicyAndProductDocsTool(missingQuery);

        var uncoveredTopicResult = await toolForUncoveredTopic.InvokeAsync(
            """{"query":"do you offer gift wrapping"}""", Context, CancellationToken.None);
        var missingArgumentResult = await toolForMissingArgument.InvokeAsync("{}", Context, CancellationToken.None);

        uncoveredTopicResult.HasData.Should().BeFalse();
        missingArgumentResult.HasData.Should().BeFalse();
        uncoveredTopicResult.RetrievedDataJson.Should().Be(missingArgumentResult.RetrievedDataJson);
    }

    [Fact]
    public async Task InvokeAsync_MissingQueryArgument_ReturnsNotFoundWithoutCallingSearch()
    {
        var search = new RecordingPolicySearch(chunks: []);
        var tool = new SearchPolicyAndProductDocsTool(search);

        var result = await tool.InvokeAsync("{}", Context, CancellationToken.None);

        result.HasData.Should().BeFalse();
        search.WasCalled.Should().BeFalse();
    }

    private sealed class RecordingPolicySearch : IPolicySearch
    {
        private readonly IReadOnlyList<PolicyChunk> _chunks;

        public RecordingPolicySearch(IReadOnlyList<PolicyChunk> chunks)
        {
            _chunks = chunks;
        }

        public string? LastQuery { get; private set; }
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<PolicyChunk>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastQuery = query;
            return Task.FromResult(_chunks);
        }
    }
}
