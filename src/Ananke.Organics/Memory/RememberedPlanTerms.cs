using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;

namespace Ananke.Organics.Memory;

/// <summary>
/// Carries a plan's terms across runs — proposing them at a later halt, never binding them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional, and it lives here rather than in the plan tier for a reason worth keeping.</b> A term
/// is scoped to one run: the person who said it is the person whose run it is, and
/// <see cref="PlanTree.Terms"/> binds it directly because of that. Across runs the claim is much
/// stronger — a preference stated last week, about a plan that may no longer resemble this one — and
/// that is exactly how a plan gets quietly narrowed by something nobody in the current run agreed to.
/// So the plan tier knows nothing about experience memory, and this is what a consumer wires when
/// they want the stronger thing.
/// </para>
/// <para>
/// <b>Recall proposes; the contract binds.</b> Nothing here writes a constraint, re-rules a node or
/// touches a tree. A remembered term is put in front of the supervisor marked as remembered, and
/// binds only if that seat adopts it — at which point it is committed <em>in this run</em>, through
/// exactly the call a person's own answer goes through.
/// </para>
/// <para>
/// <b>Two halves, and they meet only in the store.</b> <see cref="Sink"/> mirrors what a run settles
/// into <see cref="IEmpiricalMemory"/>; <see cref="Proposing"/> wraps an advisor so a later halt is
/// asked with what earlier ones settled. Wire one, both, or neither — with neither, behaviour is
/// exactly what it was before this type existed.
/// </para>
/// </remarks>
/// <param name="memory">Where terms are remembered between runs.</param>
/// <param name="tag">
/// The tag entries are written and recalled under, so a memory shared with other learning is not
/// trawled for plan terms and vice versa.
/// </param>
/// <param name="threshold">
/// How close a remembered term has to be to the halted step before it is worth showing. See the
/// remarks on <see cref="DefaultThreshold"/> — <b>this is embedder-specific and you should tune it</b>.
/// </param>
public sealed class RememberedPlanTerms(
    IEmpiricalMemory memory, string tag = "plan-term", float threshold = RememberedPlanTerms.DefaultThreshold)
{
    /// <summary>How many remembered terms one halt is shown. Enough to be useful, few enough to read.</summary>
    private const int Most = 3;

    /// <summary>
    /// The default relevance floor, and it is a <b>measurement against one embedder</b> rather than a
    /// constant anybody should trust.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recall without a floor returns everything, which is the failure mode this feature has.</b>
    /// <c>RecallOptions</c> defaults its thresholds to zero, so a store holding one term hands it back
    /// for every halt in every later run. Measured with <c>InMemoryEmbedder</c> against a term reading
    /// <em>"Naoshima stays on the trip"</em>: the step it was actually about scored <b>0.41</b>,
    /// another day of the same trip <b>0.32</b>, an unrelated software step <b>0.16</b>, and deliberate
    /// nonsense <b>0.15</b>. There is signal, and the floor under it is high, because a token-hash
    /// embedder gives almost any two strings some cosine.
    /// </para>
    /// <para>
    /// <b>0.35 separates those four on that embedder and means nothing on another one.</b> A real
    /// embedding model has its own scale, and a consumer wiring one should measure theirs the same way
    /// rather than inherit this number.
    /// </para>
    /// <para>
    /// <b>It errs high on purpose, because the two failure modes are not symmetric.</b> Recalling too
    /// little costs a feature somebody opted into and behaves exactly like not wiring this at all.
    /// Recalling too much puts preferences that have nothing to do with the halt in front of the seat
    /// that authors options — and invites it to adopt one, which narrows a plan on nobody's authority.
    /// </para>
    /// </remarks>
    public const float DefaultThreshold = 0.35f;

    /// <summary>
    /// Mirrors every term a run commits into memory, so a later run can be offered it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An event sink, because the plan tier already says this out loud.</b> `PlanTermCommitted`
    /// carries the reading, the words it was read from and who said them — everything an entry needs
    /// — so nothing in the plan tier has to learn that a memory exists in order to feed one.
    /// </para>
    /// <para>
    /// <b>A retraction follows; a waiver does not, and that was the whole of the open question.</b> It
    /// looked like *should a contradiction propagate?* — one answer, and both answers wrong half the
    /// time. It is two events wearing one name. Somebody saying <em>drop Naoshima after all</em> has
    /// changed what they want, and a later run should not offer the old preference back at them. A run
    /// letting the same term go because <em>this</em> trip could not hold it has said nothing about
    /// what they want, and forgetting it there would make them ask for it again next time — which is
    /// the exact failure terms exist to prevent. So <see cref="PlanTermEnd"/> decides, and neither
    /// case needs a policy switch.
    /// </para>
    /// </remarks>
    public IWorkflowEventSink Sink() => new Mirror(this);

    /// <summary>
    /// The same advisor, asked with what earlier runs settled in front of it.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a replacement: what a supervisor does with a halt is unchanged, and
    /// all this adds is one more thing on the page — marked as remembered, and binding nothing.
    /// </remarks>
    public PlanAdvisor Proposing(PlanAdvisor inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        return async (coordination, ct) =>
        {
            var recalled = await RecallAsync(coordination, ct).ConfigureAwait(false);

            return recalled.Count == 0
                ? await inner(coordination, ct).ConfigureAwait(false)
                : await inner(coordination with { Recalled = recalled }, ct).ConfigureAwait(false);
        };
    }

    /// <summary>What earlier runs settled that might bear on this halt.</summary>
    /// <remarks>
    /// <b>Asked about the halted step's own goal</b>, because that is what a term would have to be
    /// about to be worth adopting. Terms already in force in this run are left out: they are bound
    /// already, and offering one back would invite a supervisor to re-adopt what it cannot change.
    /// </remarks>
    public async Task<IReadOnlyList<PlanTerm>> RecallAsync(
        PlanCoordination coordination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        if (coordination.Node is not { } node)
            return [];

        var standing = coordination.Result.Tree.Terms
            .Where(t => t.InForce)
            .Select(t => t.Reading)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matches = await memory.RecallAsync(
            node.Contract.Goal,
            new RecallOptions
            {
                TopK = Most,
                Kind = EmpiricalKind.Heuristic,
                ScoreThreshold = threshold,

                // Not a quality bar — a two-valued flag. Every live term is written at exactly
                // Stated, so this admits the ones nobody took back and excludes the ones somebody
                // did, whatever the store's contradiction leaves behind. See ForgetAsync: without
                // it, a retraction only survives as long as the arithmetic happens to keep the
                // suppressed score under `threshold`, which is a number consumers are told to tune.
                MinConfidence = Stated
            },
            ct).ConfigureAwait(false);

        return
        [
            .. matches
                .Where(m => m.Entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .Select(Read)
                .Where(t => t is not null && !standing.Contains(t.Reading))!
        ];
    }

    /// <summary>
    /// The confidence every remembered term is written with, because a preference does not earn one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One value for every term in force, which is what makes it useful here.</b> Recall multiplies
    /// the cosine by confidence, so a per-entry confidence would silently rank preferences against each
    /// other — and the only thing it could rank them by is how often somebody had to repeat themselves.
    /// Pinned, it is a constant factor that cannot reorder anything, and the ordering is the embedder's
    /// alone.
    /// </para>
    /// <para>
    /// <b>It doubles as the in-force flag.</b> Contradicting an entry lowers its confidence rather than
    /// removing it, so "still at <see cref="Stated"/>" is exactly "nobody took this back" — which is
    /// what <see cref="RecallAsync"/> filters on. Neutral in value on purpose: high would assert
    /// something nobody measured, low would sink a real preference under a floor a consumer set for
    /// genuinely empirical entries sharing the store.
    /// </para>
    /// </remarks>
    private const float Stated = 0.5f;

    /// <summary>
    /// Removes a retracted term from memory, found by what it said rather than by the id it was
    /// written under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>By reading, because ids do not survive the store.</b> <c>CommitAsync</c> merges a
    /// semantically similar entry rather than duplicating it and hands back <em>that</em> entry, so
    /// the id this sink wrote may be nobody's id by the time it is retracted. What does survive is
    /// the sentence, which is also the thing a person actually took back.
    /// </para>
    /// <para>
    /// <b>"Removes" is the effect, not the mechanism, and the difference bit once.</b> Contradiction
    /// lowers an entry's confidence; it does not delete it. Left at that, a retracted term stays
    /// recallable and merely scores lower — far enough under the default floor to look forgotten, and
    /// back above a floor a consumer lowered for a different embedder. So <see cref="RecallAsync"/>
    /// excludes it by <c>MinConfidence</c> instead, which is a filter rather than an arithmetic
    /// coincidence. Saying the term again in a later run commits it afresh, which is correct: somebody
    /// who repeats a preference they once withdrew has changed their mind back.
    /// </para>
    /// </remarks>
    private async Task ForgetAsync(PlanTermContradicted retracted, CancellationToken ct)
    {
        var matches = await memory.RecallAsync(
            retracted.Reading,
            new RecallOptions { TopK = Most, Kind = EmpiricalKind.Heuristic },
            ct).ConfigureAwait(false);

        foreach (var match in matches)
        {
            if (!match.Entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;

            // Exactly what was retracted, and nothing that merely resembles it. A retraction is
            // narrow by nature — somebody took back one thing they said — and forgetting a neighbour
            // because it scored well would lose a preference nobody withdrew.
            if (string.Equals(match.Entry.Description.Summary, retracted.Reading, StringComparison.OrdinalIgnoreCase))
                await memory.ContradictAsync(match.Entry.Id, retracted.Reason, ct).ConfigureAwait(false);
        }
    }

    /// <summary>An entry as the plan tier reads one, or <see langword="null"/> if it is not one of ours.</summary>
    private static PlanTerm? Read(EmpiricalMatch match) =>
        match.Entry.Description.Summary is not { Length: > 0 } reading
            ? null
            : new PlanTerm
            {
                Id = match.Entry.Id,
                Reading = reading,

                // The words somebody actually used, carried across the run boundary intact. An entry
                // that lost them would arrive as an assertion with no source, which is the one shape
                // a term must never have.
                Said = match.Entry.Evidence.FirstOrDefault() ?? reading,
                By = match.Entry.Source,
                At = match.Entry.FirstObserved
            };

    /// <summary>Writes a term this run settled into memory, so a later run can be offered it.</summary>
    /// <remarks>
    /// <b>Committing a term somebody once retracted has to bring it back, and it did not.</b> The
    /// store merges a re-stated term into the existing entry and deliberately leaves confidence alone
    /// — so a term retracted once stayed at the contradicted value forever, and every later run that
    /// heard <em>keep Naoshima after all</em> bound it for that run and forgot it again. A person who
    /// changed their mind back would have had to say so every single time, which is the failure this
    /// whole type exists to prevent, made permanent. So the value is restored explicitly: after the
    /// commit, whatever the store did with it, an in-force term reads <see cref="Stated"/>.
    /// </remarks>
    private async Task RememberAsync(PlanTermCommitted committed, CancellationToken ct)
    {
        var stored = await memory.CommitAsync(
            new EmpiricalEntry
            {
                Id = $"{committed.PlanId}:{committed.TermId}",
                Kind = EmpiricalKind.Heuristic,
                Tags = [tag, committed.PlanId],

                // Who said it, not which plan it happened in: a reader being offered this a month
                // later needs the attribution more than the run id, and the plan is in the tags.
                Source = committed.By,
                Description = SemanticDescription.FromText(committed.Reading),

                // The person's own sentence, kept where a reader will find it beside the reading
                // rather than in place of it.
                Evidence = [committed.Said],

                // Fixed, and never earned. A preference is not an empirical claim: saying "keep
                // Naoshima" in three runs does not make it three times truer, it means the run made
                // somebody repeat themselves three times. Confidence would read that repetition as
                // evidence and rank a preference stated once below one somebody had to fight for.
                // Nothing here ever reinforces, and the store's own merge leaves confidence alone
                // (it counts the observation instead), so the value written here is the value it
                // keeps until somebody contradicts it.
                Confidence = Stated,
                ObservationCount = 1,
                FirstObserved = DateTimeOffset.UtcNow,
                LastObserved = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);

        if (stored.Confidence >= Stated)
            return;

        // Passing no reward keeps this on the store's flat path, where the adjustment is applied
        // literally — so this lands on Stated rather than somewhere near it.
        await memory.ReinforceAsync(
            stored.Id,
            new Reinforcement
            {
                NewEvidence = [],
                Source = committed.By,
                ConfidenceAdjustment = Stated - stored.Confidence
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>Routes the two events that matter, and ignores everything else on the stream.</summary>
    private sealed class Mirror(RememberedPlanTerms terms) : IWorkflowEventSink
    {
        public async ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            switch (evt)
            {
                // A waiver is not here on purpose: this plan could not hold the term, which says
                // nothing about whether it is still wanted next time.
                case PlanTermContradicted { End: PlanTermEnd.Retracted } retracted:
                    await terms.ForgetAsync(retracted, ct).ConfigureAwait(false);
                    break;

                case PlanTermCommitted committed:
                    await terms.RememberAsync(committed, ct).ConfigureAwait(false);
                    break;
            }
        }
    }
}
