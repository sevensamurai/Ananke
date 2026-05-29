using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;
using ILogger = Microsoft.Extensions.Logging.ILogger;
namespace Ananke.Platforms.Slack;

/// <summary>
/// <see cref="IPlatformResponseSink"/> implementation backed by the Slack Web _api.
/// Maps Ananke response operations to Slack <c>chat.postMessage</c>, <c>chat.update</c>,
/// <c>reactions.add</c>, etc.
/// </summary>
internal sealed class SlackResponseSink : ISlackResponseSink
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly ISlackApiClient _api;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly bool _assistantEnabled;
    private readonly string _assistantStatusLabel;
    private readonly SlackUploadMode _uploadMode;
    private readonly string _botToken;

    internal SlackResponseSink(
        ISlackApiClient api,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        SlackAdapterOptions? options = null)
    {
        _api = api;
        _httpClient = httpClient ?? SharedHttpClient;
        _logger = logger ?? NullLogger.Instance;
        _assistantEnabled = options?.EnableAssistant ?? false;
        _assistantStatusLabel = options?.AssistantStatusLabel ?? "thinking\u2026";
        _uploadMode = options?.UploadMode ?? SlackUploadMode.ExternalUrlV2;
        _botToken = options?.BotToken ?? string.Empty;
    }

    /// <summary>
    /// Test-seam constructor that accepts a pre-built <see cref="IAssistantThreadsApi"/>
    /// stub so tests do not need to implement the full <see cref="ISlackApiClient"/>.
    /// </summary>
    internal SlackResponseSink(
        IAssistantThreadsApi assistantThreadsApi,
        SlackAdapterOptions? options = null)
    {
        _api = null!;
        _httpClient = SharedHttpClient;
        _logger = NullLogger.Instance;
        _assistantEnabled = options?.EnableAssistant ?? false;
        _assistantStatusLabel = options?.AssistantStatusLabel ?? "thinking\u2026";
        _uploadMode = options?.UploadMode ?? SlackUploadMode.ExternalUrlV2;
        _botToken = options?.BotToken ?? string.Empty;
        _assistantThreadsOverride = assistantThreadsApi;
    }

    /// <summary>
    /// Test-seam constructor that accepts a pre-built <see cref="IViewsApi"/> stub so tests
    /// can verify modal open/update calls without a full <see cref="ISlackApiClient"/> fake.
    /// </summary>
    internal SlackResponseSink(
        IViewsApi viewsApi,
        SlackAdapterOptions? options = null)
    {
        _api = null!;
        _httpClient = SharedHttpClient;
        _logger = NullLogger.Instance;
        _assistantEnabled = options?.EnableAssistant ?? false;
        _assistantStatusLabel = options?.AssistantStatusLabel ?? "thinking\u2026";
        _uploadMode = options?.UploadMode ?? SlackUploadMode.ExternalUrlV2;
        _botToken = options?.BotToken ?? string.Empty;
        _viewsOverride = viewsApi;
    }

    /// <summary>
    /// Test-seam constructor that accepts a pre-built <see cref="IChatApi"/> stub so tests
    /// can capture <c>chat.postMessage</c> calls without a full <see cref="ISlackApiClient"/> fake.
    /// </summary>
    internal SlackResponseSink(
        IChatApi chatApi,
        SlackAdapterOptions? options = null)
    {
        _api = null!;
        _httpClient = SharedHttpClient;
        _logger = NullLogger.Instance;
        _assistantEnabled = options?.EnableAssistant ?? false;
        _assistantStatusLabel = options?.AssistantStatusLabel ?? "thinking\u2026";
        _uploadMode = options?.UploadMode ?? SlackUploadMode.ExternalUrlV2;
        _botToken = options?.BotToken ?? string.Empty;
        _chatOverride = chatApi;
    }

    /// <summary>
    /// Test-seam constructor that accepts a pre-built <see cref="IFilesApi"/> stub and an
    /// <see cref="HttpClient"/> so upload-mode dispatch and retry logic can be tested in isolation.
    /// </summary>
    internal SlackResponseSink(
        IFilesApi filesApi,
        HttpClient httpClient,
        SlackAdapterOptions? options = null)
    {
        _api = null!;
        _httpClient = httpClient;
        _logger = NullLogger.Instance;
        _assistantEnabled = options?.EnableAssistant ?? false;
        _assistantStatusLabel = options?.AssistantStatusLabel ?? "thinking\u2026";
        _uploadMode = options?.UploadMode ?? SlackUploadMode.ExternalUrlV2;
        _botToken = options?.BotToken ?? string.Empty;
        _filesOverride = filesApi;
    }

    private readonly IAssistantThreadsApi? _assistantThreadsOverride;
    private readonly IViewsApi? _viewsOverride;
    private readonly IChatApi? _chatOverride;
    private readonly IFilesApi? _filesOverride;
    private IAssistantThreadsApi AssistantThreadsApi =>
        _assistantThreadsOverride ?? _api.AssistantThreads;
    private IViewsApi ViewsApi =>
        _viewsOverride ?? _api.Views;
    private IChatApi ChatApi =>
        _chatOverride ?? _api.Chat;
    private IFilesApi FilesApi =>
        _filesOverride ?? _api.Files;

    /// <inheritdoc />
    public async Task<string> SendMessageAsync(string channelId, string? threadId, string text,
        CancellationToken ct = default)
    {
        var response = await ChatApi.PostMessage(new Message
        {
            Channel = channelId,
            Text = text,
            ThreadTs = threadId
        }, ct).ConfigureAwait(false);

        _logger.LogDebug("Slack: posted message {Ts} to {Channel}", response.Ts, channelId);
        return response.Ts;
    }

    /// <inheritdoc />
    public async Task UpdateMessageAsync(string channelId, string messageId, string text,
        CancellationToken ct = default)
    {
        await ChatApi.Update(new MessageUpdate
        {
            ChannelId = channelId,
            Ts = messageId,
            Text = text
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendTypingAsync(string channelId, string? threadId,
        CancellationToken ct = default)
    {
        if (_assistantEnabled && !string.IsNullOrEmpty(threadId))
            return AssistantThreadsApi.SetStatus(channelId, threadId, _assistantStatusLabel,
                cancellationToken: ct);

        // Slack does not have a public "typing indicator" API for bots outside Assistant mode.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddReactionAsync(string channelId, string messageId, string emoji,
        CancellationToken ct = default)
    {
        await _api.Reactions.AddToMessage(emoji, channelId, messageId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> SendBlocksAsync(string channelId, string? threadId, string text,
        IReadOnlyList<Block> blocks, CancellationToken ct = default)
    {
        var response = await ChatApi.PostMessage(new Message
        {
            Channel = channelId,
            Text = text,
            ThreadTs = threadId,
            Blocks = blocks.Count == 0 ? [] : [.. blocks]
        }, ct).ConfigureAwait(false);

        _logger.LogDebug("Slack: posted block message {Ts} to {Channel}", response.Ts, channelId);
        return response.Ts;
    }

    /// <inheritdoc />
    public async Task<string> SendBlocksWithMetadataAsync(string channelId, string? threadId,
        string text, IReadOnlyList<Block> blocks,
        IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        var response = await ChatApi.PostMessage(new Message
        {
            Channel = channelId,
            Text = text,
            ThreadTs = threadId,
            Blocks = blocks.Count == 0 ? [] : [.. blocks],
            MetadataJson = new MessageMetadata
            {
                EventType = "ananke_message",
                EventPayload = JObject.FromObject(metadata)
            }
        }, ct).ConfigureAwait(false);

        _logger.LogDebug("Slack: posted block message with metadata {Ts} to {Channel}",
            response.Ts, channelId);
        return response.Ts;
    }

    /// <inheritdoc />
    public async Task<string> OpenViewAsync(string triggerId, ModalViewDefinition view,
        CancellationToken ct = default)
    {
        var response = await ViewsApi.Open(triggerId, view, ct).ConfigureAwait(false);
        _logger.LogDebug("Slack: opened view {ViewId}", response.View?.Id);
        return response.View?.Id ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task UpdateViewAsync(string viewId, ModalViewDefinition view,
        CancellationToken ct = default)
    {
        await ViewsApi.UpdateByViewId(view, viewId, cancellationToken: ct)
            .ConfigureAwait(false);
        _logger.LogDebug("Slack: updated view {ViewId}", viewId);
    }

    /// <inheritdoc />
    public async Task SendEphemeralAsync(string channelId, string userId, string text,
        IReadOnlyList<Block>? blocks = null, CancellationToken ct = default)
    {
        await ChatApi.PostEphemeral(userId, new Message
        {
            Channel = channelId,
            Text = text,
            Blocks = blocks is { Count: > 0 } ? [.. blocks] : null
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> UploadFileAsync(string channelId, string? threadId, string fileName,
        byte[] content, string? title = null, string? initialComment = null,
        CancellationToken ct = default) =>
        _uploadMode == SlackUploadMode.LegacyFilesUpload
            ? UploadFileLegacyAsync(channelId, threadId, fileName, content, title, initialComment, ct)
            : UploadFileExternalV2Async(channelId, threadId, fileName, content, title, initialComment,
                retryOnExpired: true, ct);

    private async Task<string> UploadFileExternalV2Async(
        string channelId, string? threadId, string fileName, byte[] content,
        string? title, string? initialComment, bool retryOnExpired, CancellationToken ct)
    {
        try
        {
            var upload = await FilesApi
                .GetUploadUrlExternal(fileName, content.Length, string.Empty, string.Empty, ct)
                .ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl)
            {
                Content = new ByteArrayContent(content)
            };
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await FilesApi.CompleteUploadExternal(
                [new ExternalFileReference
                {
                    Id = upload.FileId,
                    Title = string.IsNullOrWhiteSpace(title) ? fileName : title
                }],
                channelId,
                initialComment ?? string.Empty,
                threadId ?? string.Empty,
                ct).ConfigureAwait(false);

            _logger.LogDebug("Slack: uploaded file {FileId} to {Channel}", upload.FileId, channelId);
            return upload.FileId;
        }
        catch (SlackException ex) when (retryOnExpired &&
            ex.ErrorCode?.Equals("expired_url", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogWarning("Slack: upload URL expired, retrying once for {File}", fileName);
            return await UploadFileExternalV2Async(channelId, threadId, fileName, content,
                title, initialComment, retryOnExpired: false, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> UploadFileLegacyAsync(
        string channelId, string? threadId, string fileName, byte[] content,
        string? title, string? initialComment, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(channelId), "channels");
        form.Add(new StringContent(fileName), "filename");
        if (!string.IsNullOrWhiteSpace(title))
            form.Add(new StringContent(title), "title");
        if (!string.IsNullOrWhiteSpace(initialComment))
            form.Add(new StringContent(initialComment), "initial_comment");
        if (!string.IsNullOrWhiteSpace(threadId))
            form.Add(new StringContent(threadId), "thread_ts");
        form.Add(new ByteArrayContent(content), "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://slack.com/api/files.upload")
        {
            Content = form
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _botToken);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = JObject.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var fileId = json["file"]?["id"]?.Value<string>() ?? string.Empty;
        _logger.LogDebug("Slack: legacy-uploaded file {FileId} to {Channel}", fileId, channelId);
        return fileId;
    }

    /// <inheritdoc />
    public async Task<string> ScheduleMessageAsync(string channelId, string? threadId, string text,
        DateTime postAt, IReadOnlyList<Block>? blocks = null, CancellationToken ct = default)
    {
        var response = await ChatApi.ScheduleMessage(new Message
        {
            Channel = channelId,
            Text = text,
            ThreadTs = threadId,
            Blocks = blocks is { Count: > 0 } ? [.. blocks] : null
        }, postAt, ct).ConfigureAwait(false);

        _logger.LogDebug("Slack: scheduled message {ScheduledMessageId} in {Channel}",
            response.ScheduledMessageId, channelId);
        return response.ScheduledMessageId;
    }

    /// <inheritdoc />
    public Task SetAssistantStatusAsync(string channelId, string threadTs, string status,
        CancellationToken ct = default) =>
        AssistantThreadsApi.SetStatus(channelId, threadTs, status, cancellationToken: ct);

    /// <inheritdoc />
    public async Task SetSuggestedPromptsAsync(string channelId, string threadTs,
        IReadOnlyList<(string Title, string Message)> prompts,
        string? title = null, CancellationToken ct = default)
    {
        var slackPrompts = prompts.Select(p => new AssistantPrompt
        {
            Title = p.Title,
            Message = p.Message
        });
        await AssistantThreadsApi.SetSuggestedPrompts(channelId, threadTs, slackPrompts, title, ct)
            .ConfigureAwait(false);
    }
}
