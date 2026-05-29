using System.Net;
using Ananke.Platforms.Slack;
using Shouldly;
using SlackNet;
using SlackNet.WebApi;

namespace Ananke.Platforms.Slack.Tests;

/// <summary>
/// Verifies that <see cref="SlackResponseSink.UploadFileAsync"/> dispatches to the
/// correct upload path based on <see cref="SlackUploadMode"/>.
/// </summary>
[TestFixture]
public sealed class SlackUploadModeTests
{
    // ── ExternalUrlV2 ────────────────────────────────────────────────────────

    [Test]
    public async Task UploadFileAsync_ExternalV2_CallsGetUploadUrlThenComplete()
    {
        const string expectedFileId = "F001";
        var files = new FakeFilesApi(uploadFileId: expectedFileId);
        var http = new FakeHttpMessageHandler(HttpStatusCode.OK);
        using var client = new HttpClient(http);
        var sink = new SlackResponseSink(files, client,
            new SlackAdapterOptions { BotToken = string.Empty, UploadMode = SlackUploadMode.ExternalUrlV2 });

        var result = await sink.UploadFileAsync("C001", null, "report.csv",
            "a,b\n1,2"u8.ToArray());

        result.ShouldBe(expectedFileId);
        files.GetUploadUrlCalls.ShouldBe(1);
        files.CompleteUploadCalls.ShouldBe(1);
        http.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task UploadFileAsync_ExternalV2_RetriesOnExpiredUrl()
    {
        const string expectedFileId = "F002";
        var files = new FakeFilesApi(uploadFileId: expectedFileId, failFirstWithExpiredUrl: true);
        var http = new FakeHttpMessageHandler(HttpStatusCode.OK);
        using var client = new HttpClient(http);
        var sink = new SlackResponseSink(files, client,
            new SlackAdapterOptions { BotToken = string.Empty, UploadMode = SlackUploadMode.ExternalUrlV2 });

        var result = await sink.UploadFileAsync("C001", null, "data.json",
            "{}"u8.ToArray());

        result.ShouldBe(expectedFileId);
        files.GetUploadUrlCalls.ShouldBe(2);
        files.CompleteUploadCalls.ShouldBe(1);
    }

    // ── LegacyFilesUpload ────────────────────────────────────────────────────

    [Test]
    public async Task UploadFileAsync_Legacy_PostsToFilesUploadEndpoint()
    {
        var files = new FakeFilesApi(uploadFileId: "ignored");
        var http = new FakeHttpMessageHandler(HttpStatusCode.OK,
            responseBody: """{"ok":true,"file":{"id":"FL01"}}""");
        using var client = new HttpClient(http);
        var sink = new SlackResponseSink(files, client,
            new SlackAdapterOptions
            {
                UploadMode = SlackUploadMode.LegacyFilesUpload,
                BotToken = "xoxb-tok"
            });

        var result = await sink.UploadFileAsync("C001", "ts.001", "photo.png",
            [1, 2, 3], "My Photo");

        result.ShouldBe("FL01");
        files.GetUploadUrlCalls.ShouldBe(0);
        http.LastRequest.ShouldNotBeNull();
        http.LastRequest!.RequestUri!.AbsoluteUri.ShouldContain("files.upload");
        http.LastRequest.Headers.Authorization!.Parameter.ShouldBe("xoxb-tok");
    }

    [Test]
    public async Task UploadFileAsync_Legacy_SelectsLegacyPath_NotExternalV2()
    {
        var files = new FakeFilesApi(uploadFileId: "ignored");
        var http = new FakeHttpMessageHandler(HttpStatusCode.OK,
            responseBody: """{"ok":true,"file":{"id":"FL02"}}""");
        using var client = new HttpClient(http);
        var sink = new SlackResponseSink(files, client,
            new SlackAdapterOptions { BotToken = string.Empty, UploadMode = SlackUploadMode.LegacyFilesUpload });

        await sink.UploadFileAsync("C001", null, "x.txt", [0]);

        files.GetUploadUrlCalls.ShouldBe(0);  // ExternalV2 was NOT used
        files.CompleteUploadCalls.ShouldBe(0);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeFilesApi : IFilesApi
    {
        private readonly string _fileId;
        private readonly bool _failFirstWithExpiredUrl;
        private bool _hasFailedOnce;

        public int GetUploadUrlCalls { get; private set; }
        public int CompleteUploadCalls { get; private set; }

        public FakeFilesApi(string uploadFileId, bool failFirstWithExpiredUrl = false)
        {
            _fileId = uploadFileId;
            _failFirstWithExpiredUrl = failFirstWithExpiredUrl;
        }

        public Task<UploadUrlExternalResponse> GetUploadUrlExternal(
            string fileName, int length,
            string altText = null!, string snippetType = null!,
            CancellationToken cancellationToken = default)
        {
            GetUploadUrlCalls++;
            if (_failFirstWithExpiredUrl && !_hasFailedOnce)
            {
                _hasFailedOnce = true;
                throw new SlackException(new ErrorResponse { Error = "expired_url" });
            }
            return Task.FromResult(new UploadUrlExternalResponse
            {
                FileId = _fileId,
                UploadUrl = "https://files.slack.com/upload/v1/fake"
            });
        }

        public Task<IList<ExternalFileReference>> CompleteUploadExternal(
            IEnumerable<ExternalFileReference> files,
            string channelId = null!, string initialComment = null!,
            string threadTs = null!, CancellationToken cancellationToken = default)
        {
            CompleteUploadCalls++;
            return Task.FromResult<IList<ExternalFileReference>>([]);
        }

        // ── Unused IFilesApi members ─────────────────────────────────────────
        public Task Delete(string fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FileAndCommentsResponse> Info(string fileId, int count = 100, int page = 1,
            string cursor = null!, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileAndCommentsResponse());

        public Task<FileListResponse> List(
            string userId = null!, string channelId = null!, string fileType = null!,
            string tsFrom = null!, IEnumerable<FileType> types = null!,
            int count = 100, int page = 1, string tsTo = null!, string cursor = null!,
            bool showFilesHiddenByLimit = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileListResponse());

        public Task<FileResponse> RevokePublicUrl(string fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileResponse());

        public Task<FileAndCommentsResponse> SharedPublicUrl(string fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileAndCommentsResponse());

        #pragma warning disable CS0618
                public Task<FileResponse> Upload(
                    string fileContents, string fileType = null!, string fileName = null!,
                    string title = null!, string initialComment = null!, string threadTs = null!,
                    IEnumerable<string> channels = null!, CancellationToken cancellationToken = default) =>
                    Task.FromResult(new FileResponse());

                public Task<FileResponse> Upload(
                    byte[] fileContents, string fileType = null!, string fileName = null!,
                    string title = null!, string initialComment = null!, string threadTs = null!,
                    IEnumerable<string> channels = null!, CancellationToken cancellationToken = default) =>
                    Task.FromResult(new FileResponse());

                public Task<FileResponse> Upload(
                    Stream fileContents, string fileType = null!, string fileName = null!,
                    string title = null!, string initialComment = null!, string threadTs = null!,
                    IEnumerable<string> channels = null!, CancellationToken cancellationToken = default) =>
                    Task.FromResult(new FileResponse());

                public Task<FileResponse> UploadSnippet(
                    string content, string fileType = null!, string filename = null!,
                    string title = null!, string initialComment = null!, string threadTs = null!,
                    IEnumerable<string> channels = null!, CancellationToken cancellationToken = default) =>
                    Task.FromResult(new FileResponse());
        #pragma warning restore CS0618

        public Task<ExternalFileReference> Upload(FileUpload file,
            string channelId = null!, string threadTs = null!,
            string initialComment = null!, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalFileReference { Id = string.Empty });

        public Task<IList<ExternalFileReference>> Upload(IEnumerable<FileUpload> files,
            string channelId = null!, string threadTs = null!,
            string initialComment = null!, CancellationToken cancellationToken = default) =>
            Task.FromResult<IList<ExternalFileReference>>([]);
    }

    private sealed class FakeHttpMessageHandler(
        HttpStatusCode statusCode,
        string responseBody = "") : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    string.IsNullOrEmpty(responseBody)
                        ? """{"ok":true}"""
                        : responseBody)
            });
        }
    }
}
