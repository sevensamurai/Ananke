using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;

namespace Ananke.Orchestration.Bedrock;

/// <summary>
/// Signs outgoing requests with AWS Signature Version 4, resolving credentials from an
/// <see cref="AWSCredentials"/> chain on every request so rotating and session credentials work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Bedrock's OpenAI-compatible and Anthropic-native endpoints accept
/// either a Bedrock API key or SigV4. Organisations that forbid long-lived bearer tokens can only
/// use SigV4 — and neither the OpenAI nor the Anthropic .NET client can sign a request. This handler
/// is the whole reason <c>Ananke.Orchestration.Bedrock</c> is a package rather than a guide.
/// </para>
/// <para>
/// <b>Why the signing is written out here.</b> <c>AWSSDK.Core</c> ships SigV4 signers, but they live
/// in <c>Amazon.Runtime.Internal.Auth</c> and operate on the SDK's own <c>IRequest</c> and
/// <c>IClientConfig</c> abstractions, not on <see cref="HttpRequestMessage"/>. Depending on an
/// internal namespace to reach them would be worse than implementing the documented algorithm.
/// <c>AWSSDK.Core</c> is still used for what it is good at: resolving the credential chain.
/// </para>
/// </remarks>
public sealed class AwsSigV4Handler : DelegatingHandler
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private static readonly char[] PathSeparator = ['/'];

    private readonly AWSCredentials _credentials;
    private readonly string _region;
    private readonly string _service;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a handler that signs for <paramref name="region"/> and <paramref name="service"/>.</summary>
    /// <param name="credentials">Credential source. Resolved per request, so refreshing chains work.</param>
    /// <param name="region">AWS region forming part of the credential scope.</param>
    /// <param name="service">AWS service name forming part of the credential scope.</param>
    /// <param name="timeProvider">Clock used for the request timestamp. Defaults to <see cref="TimeProvider.System"/>.</param>
    public AwsSigV4Handler(
        AWSCredentials credentials,
        string region,
        string service,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        _credentials = credentials;
        _region = region;
        _service = service;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await SignAsync(request, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal async Task SignAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is null)
            throw new InvalidOperationException("Cannot sign a request with no RequestUri.");

        var credentials = await _credentials.GetCredentialsAsync().ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var payloadHash = ToHex(SHA256.HashData(body));

        // Headers that must be present before the canonical form is computed, because they are
        // themselves signed.
        request.Headers.Host ??= request.RequestUri.IdnHost;
        SetHeader(request, "x-amz-date", amzDate);
        SetHeader(request, "x-amz-content-sha256", payloadHash);
        if (!string.IsNullOrEmpty(credentials.Token))
            SetHeader(request, "x-amz-security-token", credentials.Token);

        var signedHeaderPairs = CollectHeaders(request);
        var signedHeaderNames = string.Join(";", signedHeaderPairs.Select(h => h.Key));

        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            CanonicalPath(request.RequestUri),
            CanonicalQuery(request.RequestUri),
            string.Concat(signedHeaderPairs.Select(h => $"{h.Key}:{h.Value}\n")),
            signedHeaderNames,
            payloadHash);

        var credentialScope = $"{dateStamp}/{_region}/{_service}/aws4_request";
        var stringToSign = string.Join('\n',
            Algorithm,
            amzDate,
            credentialScope,
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = DeriveSigningKey(credentials.SecretKey, dateStamp);
        var signature = ToHex(HmacSha256(signingKey, stringToSign));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            Algorithm,
            $"Credential={credentials.AccessKey}/{credentialScope}, " +
            $"SignedHeaders={signedHeaderNames}, Signature={signature}");
    }

    // ── canonicalisation ─────────────────────────────────────────────

    private static List<KeyValuePair<string, string>> CollectHeaders(HttpRequestMessage request)
    {
        // NonValidated, because SigV4 signs the header value *as it goes on the wire* and only this
        // view reports it. Enumerating the typed collections yields parsed values, and rejoining
        // those is not the same string: User-Agent parses into one value per product token and is
        // serialised space-separated, so "OpenAI/2.12.0 (.NET 10.0.10; Ubuntu 26.04)" comes back as
        // two entries that a comma-join turns into a value the service never received. AWS then
        // computes a different signature and returns 401 — see the regression test.
        var headers = request.Headers.NonValidated
            .Concat(request.Content?.Headers.NonValidated
                ?? Enumerable.Empty<KeyValuePair<string, HeaderStringValues>>())
            .Select(h => new KeyValuePair<string, string>(
                h.Key.ToLowerInvariant(),
                CollapseWhitespace(h.Value.ToString())))
            .ToList();

        // Authorization is never signed; a stale one from a retried request must not leak in.
        headers.RemoveAll(h => h.Key == "authorization");

        return [.. headers.OrderBy(h => h.Key, StringComparer.Ordinal)];
    }

    private static string CollapseWhitespace(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.Contains("  ", StringComparison.Ordinal))
            return trimmed;

        var builder = new StringBuilder(trimmed.Length);
        var previousWasSpace = false;
        foreach (var c in trimmed)
        {
            if (c == ' ' && previousWasSpace)
                continue;

            builder.Append(c);
            previousWasSpace = c == ' ';
        }
        return builder.ToString();
    }

    private static string CanonicalPath(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.Length == 0)
            return "/";

        // Each segment is encoded, the separators are not. Bedrock's paths are already simple, but
        // a model id in a path segment can contain characters that must survive verbatim.
        var segments = path.Split(PathSeparator);
        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static string CanonicalQuery(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        if (query.Length == 0)
            return string.Empty;

        var pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var split = part.Split('=', 2);
                var key = Uri.EscapeDataString(Uri.UnescapeDataString(split[0]));
                var value = split.Length > 1
                    ? Uri.EscapeDataString(Uri.UnescapeDataString(split[1]))
                    : string.Empty;
                return (key, value);
            })
            .OrderBy(p => p.key, StringComparer.Ordinal)
            .ThenBy(p => p.value, StringComparer.Ordinal);

        return string.Join('&', pairs.Select(p => $"{p.key}={p.value}"));
    }

    // ── signing key ──────────────────────────────────────────────────

    private byte[] DeriveSigningKey(string secretKey, string dateStamp)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secretKey}"), dateStamp);
        var kRegion = HmacSha256(kDate, _region);
        var kService = HmacSha256(kRegion, _service);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string ToHex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
