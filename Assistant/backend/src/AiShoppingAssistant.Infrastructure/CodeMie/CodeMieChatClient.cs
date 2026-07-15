using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiShoppingAssistant.Core.Ports;
using Microsoft.Extensions.Options;

namespace AiShoppingAssistant.Infrastructure.CodeMie;

/// <summary>
/// <see cref="IChatModel"/> implementation reaching Claude Sonnet 5 through
/// EPAM's CodeMie gateway (research.md §2), forwarding Claude's native
/// tool-use (Messages API) request/response shape largely as-is so the
/// Reason/Act/Observe loop in <c>AiShoppingAssistant.Core.ReAct</c> stays
/// provider-agnostic behind <see cref="IChatModel"/>.
///
/// No live CodeMie credentials exist in this repository/environment. This
/// client is structurally complete (real HTTP call shape, real
/// request/response mapping) and configured entirely via
/// <see cref="CodeMieOptions"/> (bound from the <c>CodeMie</c> configuration
/// section — see appsettings.Development.json), so it can be pointed at a
/// real gateway and exercised without any code changes once credentials are
/// available, and swapped out behind <see cref="IChatModel"/> in tests.
///
/// Round-trips Claude's own <c>tool_use</c> assistant-turn content blocks back
/// into the message history: <see cref="AiShoppingAssistant.Core.ReAct.ReActLoop"/>
/// echoes each tool call as a <see cref="ChatMessageRole.ToolUse"/> message before
/// its <see cref="ChatMessageRole.ToolResult"/>, and <see cref="MapToMessageDto"/>/
/// <see cref="MergeConsecutiveSameRole"/> below fold consecutive same-role messages
/// into single wire-format turns, since the Messages API requires strictly
/// alternating user/assistant turns (one turn may carry multiple content blocks).
/// </summary>
public sealed class CodeMieChatClient : IChatModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly CodeMieOptions _options;

    public CodeMieChatClient(HttpClient httpClient, IOptions<CodeMieOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ChatCompletionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException(
                "CodeMie:BaseUrl is not configured. Set CodeMie:BaseUrl/ApiKey/Model (see appsettings.Development.json) " +
                "before the chat model can be called.");
        }

        var payload = BuildRequestPayload(request);
        var requestUri = new Uri(new Uri(_options.BaseUrl, UriKind.Absolute), "messages");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"CodeMie gateway returned {(int)httpResponse.StatusCode} {httpResponse.StatusCode}: {errorBody}");
        }

        var responseDto = await httpResponse.Content
            .ReadFromJsonAsync<CodeMieChatResponseDto>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("CodeMie gateway returned an empty response body.");

        return MapToCompletionResult(responseDto);
    }

    private CodeMieChatRequestDto BuildRequestPayload(ChatRequest request)
    {
        return new CodeMieChatRequestDto
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = request.SystemPrompt,
            Messages = MergeConsecutiveSameRole(request.Messages.Select(MapToMessageDto).ToList()),
            Tools = request.Tools.Select(MapToToolDto).ToList(),
        };
    }

    private static CodeMieMessageDto MapToMessageDto(ChatMessage message)
    {
        return message.Role switch
        {
            ChatMessageRole.User => new CodeMieMessageDto
            {
                Role = "user",
                Content = [new CodeMieContentBlockDto { Type = "text", Text = message.Content }],
            },
            ChatMessageRole.Assistant => new CodeMieMessageDto
            {
                Role = "assistant",
                Content = [new CodeMieContentBlockDto { Type = "text", Text = message.Content }],
            },
            ChatMessageRole.ToolResult => new CodeMieMessageDto
            {
                Role = "user",
                Content =
                [
                    new CodeMieContentBlockDto
                    {
                        Type = "tool_result",
                        ToolUseId = message.ToolCallId ?? message.ToolName ?? "unknown",
                        ToolResultContent = message.Content,
                    },
                ],
            },
            ChatMessageRole.ToolUse => new CodeMieMessageDto
            {
                Role = "assistant",
                Content =
                [
                    new CodeMieContentBlockDto
                    {
                        Type = "tool_use",
                        Id = message.ToolCallId,
                        Name = message.ToolName,
                        Input = JsonNode.Parse(message.Content),
                    },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Role, "Unsupported chat message role."),
        };
    }

    /// <summary>
    /// Folds consecutive same-role messages into a single wire-format turn by
    /// concatenating their content blocks — the Messages API requires strictly
    /// alternating user/assistant turns, while <see cref="AiShoppingAssistant.Core.ReAct.ReActLoop"/>'s
    /// message list has one entry per tool_use/tool_result block.
    /// </summary>
    private static List<CodeMieMessageDto> MergeConsecutiveSameRole(List<CodeMieMessageDto> messages)
    {
        var merged = new List<CodeMieMessageDto>();
        foreach (var message in messages)
        {
            var last = merged.Count > 0 ? merged[^1] : null;
            if (last is not null && last.Role == message.Role)
            {
                last.Content.AddRange(message.Content);
            }
            else
            {
                merged.Add(message);
            }
        }

        return merged;
    }

    private static CodeMieToolDto MapToToolDto(ToolDefinition tool)
    {
        return new CodeMieToolDto
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = JsonNode.Parse(tool.InputSchemaJson),
        };
    }

    private static ChatCompletionResult MapToCompletionResult(CodeMieChatResponseDto response)
    {
        var toolCalls = response.Content
            .Where(block => block.Type == "tool_use")
            .Select(block => new ToolCallRequest(
                block.Id ?? Guid.NewGuid().ToString("n"),
                block.Name ?? string.Empty,
                block.Input.ValueKind == JsonValueKind.Undefined ? "{}" : block.Input.GetRawText()))
            .ToList();

        if (toolCalls.Count > 0)
        {
            return new ChatCompletionResult(TextResponse: null, toolCalls);
        }

        var text = string.Concat(response.Content
            .Where(block => block.Type == "text")
            .Select(block => block.Text ?? string.Empty));

        return new ChatCompletionResult(text, Array.Empty<ToolCallRequest>());
    }
}

internal sealed class CodeMieChatRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    public string System { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<CodeMieMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("tools")]
    public List<CodeMieToolDto> Tools { get; set; } = [];
}

internal sealed class CodeMieMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public List<CodeMieContentBlockDto> Content { get; set; } = [];
}

internal sealed class CodeMieContentBlockDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Tool-use block id, set only when <see cref="Type"/> is <c>tool_use</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Tool name, set only when <see cref="Type"/> is <c>tool_use</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Tool arguments, set only when <see cref="Type"/> is <c>tool_use</c>.</summary>
    [JsonPropertyName("input")]
    public JsonNode? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public string? ToolResultContent { get; set; }
}

internal sealed class CodeMieToolDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("input_schema")]
    public JsonNode? InputSchema { get; set; }
}

internal sealed class CodeMieChatResponseDto
{
    [JsonPropertyName("content")]
    public List<CodeMieResponseContentBlockDto> Content { get; set; } = [];
}

internal sealed class CodeMieResponseContentBlockDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }
}
