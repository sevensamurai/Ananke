using System.Net.Http.Headers;
using System.Net;
using System.Text;
using Amazon.Runtime;
using Ananke.Orchestration.Bedrock;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Bedrock.Tests;

/// <summary>
/// Verifies the SigV4 implementation against AWS's published worked example, then covers the parts
/// of the algorithm that example does not exercise.
/// </summary>
/// <remarks>
/// The golden vector matters more than the property tests: SigV4 is easy to implement in a way that
/// looks right and produces a signature the service rejects, and the failure mode is a 403 with no
/// diagnostic. Everything else here guards a specific way the canonical form can silently drift.
/// </remarks>
[TestFixture]
public sealed class AwsSigV4HandlerTests
{
    // Credentials and timestamp from AWS's "get-vanilla" example in the Signature Version 4 test
    // suite. See Signs_the_aws_worked_example_byte_for_byteAsync for how the expected signature
    // relates to the published one.
    private const string ExampleAccessKey = "AKIDEXAMPLE";
    private const string ExampleSecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private static readonly DateTimeOffset ExampleInstant =
        new(2015, 8, 30, 12, 36, 0, TimeSpan.Zero);

    /// <summary>
    /// Golden vector. The expected signature was <b>not</b> taken from this implementation's output
    /// — that would make the test circular. It was produced by a second, independent implementation
    /// written from the SigV4 specification, which reproduces AWS's published <c>get-vanilla</c>
    /// signature <c>5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31</c> exactly for
    /// the published header set (<c>host;x-amz-date</c>). Feeding that same implementation the
    /// header set this handler actually sends — which adds <c>x-amz-content-sha256</c> — yields the
    /// value asserted below. Two independent implementations agreeing on the same inputs is the
    /// evidence; the published vector is what makes the second one trustworthy.
    /// </summary>
    [Test]
    public async Task Signs_the_aws_worked_example_byte_for_byteAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.amazonaws.com/");
        var handler = Handler(region: "us-east-1", service: "service");

        await handler.SignAsync(request, CancellationToken.None);

        var auth = request.Headers.Authorization!;
        auth.Scheme.ShouldBe("AWS4-HMAC-SHA256");
        auth.Parameter!.ShouldContain($"Credential={ExampleAccessKey}/20150830/us-east-1/service/aws4_request");
        auth.Parameter!.ShouldContain("SignedHeaders=host;x-amz-content-sha256;x-amz-date");
        auth.Parameter!.ShouldContain(
            "Signature=726c5c4879a6b4ccbbd3b24edbd6b8826d34f87450fbbf4e85546fc7ba9c1642");
    }

    [Test]
    public async Task Signs_the_date_and_payload_hash_headersAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.amazonaws.com/")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        await Handler().SignAsync(request, CancellationToken.None);

        request.Headers.GetValues("x-amz-date").Single().ShouldBe("20150830T123600Z");
        // SHA-256 of "{}" — the payload hash must cover the body, not be the empty-string hash.
        request.Headers.GetValues("x-amz-content-sha256").Single()
            .ShouldBe("44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a");
        request.Headers.Authorization!.Parameter!.ShouldContain("SignedHeaders=");
    }

    [Test]
    public async Task Includes_the_session_token_when_credentials_are_temporaryAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.amazonaws.com/");
        var handler = Handler(token: "session-token-value");

        await handler.SignAsync(request, CancellationToken.None);

        request.Headers.GetValues("x-amz-security-token").Single().ShouldBe("session-token-value");
        // A signed request that omits the token from SignedHeaders is rejected by AWS.
        request.Headers.Authorization!.Parameter!.ShouldContain("x-amz-security-token");
    }

    [Test]
    public async Task Does_not_sign_a_stale_authorization_headerAsync()
    {
        // A retried request arrives carrying the previous attempt's Authorization. Signing over it
        // would produce a signature the service cannot reproduce.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.amazonaws.com/");
        var handler = Handler(region: "us-east-1", service: "service");

        await handler.SignAsync(request, CancellationToken.None);
        var first = request.Headers.Authorization!.Parameter;

        await handler.SignAsync(request, CancellationToken.None);

        request.Headers.Authorization!.Parameter!.ShouldBe(first);
    }

    [Test]
    public async Task Sorts_query_parameters_canonicallyAsync()
    {
        // Canonical order is by encoded key, not by the order they appear in the URI.
        var a = new HttpRequestMessage(HttpMethod.Get, "https://example.amazonaws.com/?b=2&a=1");
        var b = new HttpRequestMessage(HttpMethod.Get, "https://example.amazonaws.com/?a=1&b=2");

        await Handler().SignAsync(a, CancellationToken.None);
        await Handler().SignAsync(b, CancellationToken.None);

        a.Headers.Authorization!.Parameter!.ShouldBe(b.Headers.Authorization!.Parameter);
    }

    [Test]
    public async Task Resolves_credentials_on_every_requestAsync()
    {
        // A refreshing chain must be consulted per request; caching at construction defeats it.
        var credentials = new CountingCredentials(ExampleAccessKey, ExampleSecretKey);
        var handler = new AwsSigV4Handler(credentials, "us-east-1", "bedrock", Clock());

        await handler.SignAsync(new HttpRequestMessage(HttpMethod.Get, "https://x.amazonaws.com/"), default);
        await handler.SignAsync(new HttpRequestMessage(HttpMethod.Get, "https://x.amazonaws.com/"), default);

        credentials.Resolutions.ShouldBe(2);
    }

    [Test]
    public async Task Signs_requests_sent_through_the_pipelineAsync()
    {
        var capture = new CapturingHandler();
        var handler = new AwsSigV4Handler(
            Credentials(), "us-east-1", "bedrock", Clock())
        { InnerHandler = capture };

        using var client = new HttpClient(handler);
        await client.GetAsync(new Uri("https://bedrock-runtime.us-east-1.amazonaws.com/openai/v1/models"));

        capture.Authorization!.ShouldStartWith("AWS4-HMAC-SHA256 Credential=");
    }

    [Test]
    public void Rejects_missing_arguments()
    {
        Should.Throw<ArgumentNullException>(() => new AwsSigV4Handler(null!, "r", "s"));
        Should.Throw<ArgumentException>(() => new AwsSigV4Handler(Credentials(), " ", "s"));
        Should.Throw<ArgumentException>(() => new AwsSigV4Handler(Credentials(), "r", " "));
    }

    /// <summary>
    /// Regression for a 401 that only AWS could reveal. <c>User-Agent</c> parses into one value per
    /// product token, and <c>HttpRequestMessage.Headers</c> hands those back separately; the wire
    /// format joins them with a <b>space</b>. Signing a comma-joined value produces a signature that
    /// is internally consistent and that AWS rejects with <i>"The request signature we calculated
    /// does not match the signature you provided"</i> — the header set and the body are right, so
    /// nothing local looks wrong.
    /// </summary>
    /// <remarks>
    /// Expected signature produced by the same independent implementation described on
    /// <see cref="Signs_the_aws_worked_example_byte_for_byteAsync"/>, which reproduces AWS's
    /// published <c>get-vanilla</c> vector exactly. The comma-joined form it replaced would have
    /// signed <c>df3258ad829b70264bcf0f63510367489484f677f3ff176c845a941b0597bb59</c>.
    /// </remarks>
    [Test]
    public async Task Signs_a_multi_token_user_agent_the_way_it_goes_on_the_wireAsync()
    {
        var content = new StringContent("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://bedrock-runtime.us-west-2.amazonaws.com/openai/v1/chat/completions")
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Exactly how the OpenAI client builds it: a product token plus a comment, two values.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OpenAI", "2.12.0"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(.NET 10.0.10; Ubuntu 26.04 LTS)"));
        request.Headers.UserAgent.Count.ShouldBe(2, "the premise of this test is a split User-Agent");

        await Handler(region: "us-west-2").SignAsync(request, CancellationToken.None);

        var parameter = request.Headers.Authorization!.Parameter!;
        parameter.ShouldContain(
            "SignedHeaders=accept;content-length;content-type;host;user-agent;" +
            "x-amz-content-sha256;x-amz-date");
        parameter.ShouldContain(
            "Signature=6baabb8b5155454cd734e203c8d3520c59b0f6ad956710f1dd94c1d0d7b5857f");
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static AwsSigV4Handler Handler(
        string region = "us-east-1", string service = "bedrock", string? token = null) =>
        new(Credentials(token), region, service, Clock());

    private static AWSCredentials Credentials(string? token = null) =>
        token is null
            ? new BasicAWSCredentials(ExampleAccessKey, ExampleSecretKey)
            : new SessionAWSCredentials(ExampleAccessKey, ExampleSecretKey, token);

    private static TimeProvider Clock() => new FakeTimeProvider(ExampleInstant);

    private sealed class CountingCredentials(string accessKey, string secretKey) : AWSCredentials
    {
        public int Resolutions { get; private set; }

        public override ImmutableCredentials GetCredentials()
        {
            Resolutions++;
            return new ImmutableCredentials(accessKey, secretKey, null);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
