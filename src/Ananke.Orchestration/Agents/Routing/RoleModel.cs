namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// The model one role runs on, and where that id came from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it came from is not bookkeeping.</b> Two roles ending up on one model is either a
/// decision or an accident, and only the source can tell them apart: a shared setting that both
/// roles fell back to collapses them silently, while naming the same id for each role is somebody
/// saying they meant it. A wiring that cannot distinguish those has to either refuse both or allow
/// both, and each of those is wrong half the time.
/// </para>
/// </remarks>
public sealed record RoleModel
{
    /// <summary>The role this model was hired for.</summary>
    public required string Role { get; init; }

    /// <summary>The model id it resolved to.</summary>
    public required string Id { get; init; }

    /// <summary>What supplied it — a setting's name, or where the default came from.</summary>
    public required string From { get; init; }

    /// <summary>What to set to choose a model for this role, whether or not it was set.</summary>
    /// <remarks>
    /// Carried even when unused, because the message worth writing is not <em>this collapsed</em>
    /// but <em>this collapsed and here is the thing to set</em>. A caller that had to reconstruct it
    /// would be reconstructing something the resolution already knew.
    /// </remarks>
    public required string Setting { get; init; }

    /// <summary>Whether it was set for <em>this</em> role rather than inherited or defaulted.</summary>
    public required bool Chosen { get; init; }
}

/// <summary>
/// Which model each role runs on, resolved once where both are known.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a wire-up concern and it belongs nowhere else.</b> A supervision reasons about roles;
/// a model's <em>name</em> is not something the plan tier should be able to see, and making it
/// suspicious of its own configuration would answer the question in the wrong place. Whoever builds
/// the wiring chose both models and knows both ids — so the question is answerable exactly where it
/// arises, and this is that place.
/// </para>
/// <para>
/// <b>It reads no configuration itself.</b> Values are passed in already read, so this stays a pure
/// function of what the caller found: testable, host-agnostic, and unable to disagree with whatever
/// actually configured the run.
/// </para>
/// </remarks>
public static class RoleModels
{
    /// <summary>Where a default comes from when nothing was set.</summary>
    public const string Catalogue = "the catalogue's current id";

    /// <summary>
    /// The model a role runs on: what was set for the role, then what is shared, then the default.
    /// </summary>
    /// <param name="role">The role being hired for.</param>
    /// <param name="forRole">What was set for this role specifically, if anything.</param>
    /// <param name="forRoleName">What that setting is called, for a message that can be acted on.</param>
    /// <param name="shared">A setting both roles fall back to, if there is one.</param>
    /// <param name="sharedName">What <em>that</em> is called.</param>
    /// <param name="fallback">The id to use when nothing was set.</param>
    /// <remarks>
    /// <b>The per-role setting wins, and the shared one beats the default</b> — a model id is the
    /// thing most likely to age, and an id already in someone's configuration is the one their
    /// account is known to serve. The order is what makes the collapse possible, which is why the
    /// source travels with the answer.
    /// </remarks>
    public static RoleModel Resolve(
        string role,
        string? forRole,
        string forRoleName,
        string? shared,
        string sharedName,
        string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(forRoleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        if (!string.IsNullOrWhiteSpace(forRole))
            return new RoleModel
            {
                Role = role,
                Id = forRole,
                From = forRoleName,
                Setting = forRoleName,
                Chosen = true
            };

        return string.IsNullOrWhiteSpace(shared)
            ? new RoleModel
            {
                Role = role,
                Id = fallback,
                From = Catalogue,
                Setting = forRoleName,
                Chosen = false
            }
            : new RoleModel
            {
                Role = role,
                Id = shared,
                From = sharedName,
                Setting = forRoleName,
                Chosen = false
            };
    }

    /// <summary>
    /// Why a pair that was supposed to be an escalation is not one, or <see langword="null"/> when
    /// it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Equal ids are only a fault when nobody asked for them.</b> If each role was named
    /// explicitly and the two names match, somebody meant it — comparing a plan tier against itself
    /// is a legitimate experiment. If either fell back to something shared, the split the wiring
    /// expresses collapsed without anyone deciding to, and every finding from that run is about one
    /// model wearing two hats.
    /// </para>
    /// <para>
    /// <b>It returns the sentence rather than throwing.</b> What to do about it is the caller's —
    /// refuse to start, warn, or record it beside the results — and a message that names the setting
    /// responsible is the part they cannot easily write themselves.
    /// </para>
    /// </remarks>
    public static string? Collapsed(RoleModel first, RoleModel second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase))
            return null;

        if (first.Chosen && second.Chosen)
            return null;

        var inherited = first.Chosen ? second : first;

        return $"The '{first.Role}' and '{second.Role}' roles both resolved to '{first.Id}', and "
            + $"'{inherited.Role}' was not asked for it — it came from {inherited.From}. These two "
            + "roles exist to be different models, so a run wired this way measures one model twice "
            + $"and reports it as an escalation. Set {inherited.Setting} to the model that role "
            + $"should run on, or clear {inherited.From} to fall back to the catalogue's two.";
    }

    /// <summary>Whether the two roles actually got different models.</summary>
    public static bool Escalated(RoleModel first, RoleModel second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return !string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase);
    }
}
