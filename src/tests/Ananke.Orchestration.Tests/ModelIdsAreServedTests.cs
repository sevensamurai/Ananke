using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Asks each provider which models it serves, and compares that with the ids this repo names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The release-time rule, made mechanical.</b> Provider line-ups turn over every few months, and
/// until now "check whether the catalogue needs new entries" was a note in a contributing guide —
/// which is to say, a memory exercise. Four live runs died at node 1 on ids the account did not
/// serve before anybody thought to ask the API.
/// </para>
/// <para>
/// <b>Explicit, and it needs keys.</b> It talks to the real providers, so it is not part of an
/// ordinary <c>dotnet test</c>: run it with <c>--filter TestCategory=Live</c> when preparing a
/// release, or whenever a model id is added. Listing models costs nothing and generates no tokens.
/// </para>
/// <para>
/// <b>What it deliberately does not check.</b> Open-weight models are named here because they are
/// self-hosted — nobody's hosted API has to list <c>gemma-4</c> for it to be a real thing to run —
/// and specialty non-chat models are outside what these catalogues describe. Both are exempted by
/// name below, with the reason, rather than by a pattern that would quietly swallow a real gap.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Talks to the providers' model-listing endpoints; needs OPENAI_API_KEY / GOOGLE_API_KEY.")]
public class ModelIdsAreServedTests
{
    /// <summary>
    /// Ids that are real without a hosted API listing them, and the reason each is here.
    /// </summary>
    private static readonly Dictionary<string, string> NotExpectedFromAListing = new(StringComparer.OrdinalIgnoreCase)
    {
        [Models.Google.Gemma4] = "open-weight, run locally — no hosted listing is the authority on it",
        [Models.Google.Lyria3] = "music generation, outside what these catalogues describe",
    };

    [Test]
    public async Task OpenAI_EveryIdThisRepoNames_IsServedByTheAccount()
    {
        var key = Key("OPENAI_API_KEY");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", key);

        var served = await IdsAsync(http, "https://api.openai.com/v1/models", "data", "id")
            .ConfigureAwait(false);

        Missing(typeof(Models.OpenAI), served).ShouldBeEmpty();
    }

    [Test]
    public async Task Google_EveryIdThisRepoNames_IsServedByTheApi()
    {
        var key = Key("GOOGLE_API_KEY");

        using var http = new HttpClient();
        var served = await IdsAsync(
            http,
            $"https://generativelanguage.googleapis.com/v1beta/models?key={key}&pageSize=200",
            "models", "name").ConfigureAwait(false);

        // The API returns "models/gemini-3.6-flash"; the wire id is the last segment.
        var ids = served.Select(id => id[(id.LastIndexOf('/') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Missing(typeof(Models.Google), ids).ShouldBeEmpty();
    }

    /// <summary>Every constant on <paramref name="provider"/> that the listing does not contain.</summary>
    /// <remarks>
    /// Deprecated ids are checked too, and on purpose: <see cref="ModelStatus.Deprecated"/> means
    /// "still callable, prefer the replacement", so an id that has actually stopped being served is
    /// a status this repo has wrong — which is the more dangerous of the two, because deprecated
    /// reads as safe.
    /// </remarks>
    private static List<string> Missing(Type provider, IReadOnlySet<string> served) =>
    [
        .. provider.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !served.Contains(id) && !NotExpectedFromAListing.ContainsKey(id))
    ];

    private static async Task<HashSet<string>> IdsAsync(
        HttpClient http, string url, string collection, string field)
    {
        var payload = await http.GetFromJsonAsync<JsonElement>(url).ConfigureAwait(false);

        return payload.TryGetProperty(collection, out var entries)
            ? [.. entries.EnumerateArray().Select(e => e.GetProperty(field).GetString()!)]
            : [];
    }

    private static string Key(string name) => Keys.Require(name);
}
