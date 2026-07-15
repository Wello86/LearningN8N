using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiShoppingAssistant.Core.Ports;
using Microsoft.Extensions.Options;

namespace AiShoppingAssistant.Infrastructure.CodeMie;

/// <summary>
/// <see cref="IEmbeddingModel"/> implementation reaching EPAM's CodeMie
/// gateway's embedding endpoint (research.md §3) — used only at seed time to
/// pre-compute <see cref="Core.Entities.KnowledgeDocument.Embedding"/>, never
/// on the query path (queries are embedded through the same client at search
/// time by <c>KnowledgeDocumentRepository</c>, per research.md §3's "no
/// re-embedding on every query" note referring to document content, not the
/// query string itself).
///
/// No live CodeMie credentials exist in this repository/environment — see
/// <see cref="CodeMieChatClient"/>'s equivalent note.
/// </summary>
public sealed class CodeMieEmbeddingClient : IEmbeddingModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly CodeMieOptions _options;

    public CodeMieEmbeddingClient(HttpClient httpClient, IOptions<CodeMieOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException(
                "CodeMie:BaseUrl is not configured. Set CodeMie:BaseUrl/ApiKey/EmbeddingModel (see appsettings.Development.json) " +
                "before the embedding model can be called.");
        }

        var payload = new CodeMieEmbeddingRequestDto { Model = _options.EmbeddingModel, Input = text };
        var requestUri = new Uri(new Uri(_options.BaseUrl, UriKind.Absolute), "embeddings");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var responseDto = await httpResponse.Content
            .ReadFromJsonAsync<CodeMieEmbeddingResponseDto>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("CodeMie gateway returned an empty embedding response body.");

        return responseDto.Data.FirstOrDefault()?.Embedding
            ?? throw new InvalidOperationException("CodeMie gateway returned no embedding data.");
    }
}

internal sealed class CodeMieEmbeddingRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;
}

internal sealed class CodeMieEmbeddingResponseDto
{
    [JsonPropertyName("data")]
    public List<CodeMieEmbeddingDataDto> Data { get; set; } = [];
}

internal sealed class CodeMieEmbeddingDataDto
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}
