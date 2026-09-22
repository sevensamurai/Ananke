using System.Text.Json.Serialization;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// One state of a plan: every node it contains, and why it replaced the version before it.
/// </summary>
/// <remarks>
/// Unchanged subtrees are <em>shared</em> rather than copied. Nodes are immutable records held by
/// reference, so re-ruling one subtree leaves every node outside it pointing at the same instance
/// the previous version used.
/// </remarks>
public sealed record PlanVersion
{
    /// <summary>Version number, starting at 1 and increasing by one each time a version is minted.</summary>
    public required int Number { get; init; }

    /// <summary>The root node's id.</summary>
    public required string RootId { get; init; }

    /// <summary>Every node in this version, by id.</summary>
    public required IReadOnlyDictionary<string, PlanNode> Nodes { get; init; }

    /// <summary>When this version was minted.</summary>
    public required DateTimeOffset MintedAt { get; init; }

    /// <summary>
    /// The node whose criteria were re-ruled to produce this version, or <see langword="null"/> for
    /// the first version.
    /// </summary>
    public string? ReRuledNodeId { get; init; }

    /// <summary>
    /// Nodes the re-ruling decided were no longer needed. They are gone from this version and still
    /// present in every version before it.
    /// </summary>
    /// <remarks>
    /// This is what a status flag destroys. "Cancelled" records that something stopped; the lineage
    /// records what it was, what it was for, and which change of plan made it unnecessary — and it
    /// is also the only way to say how much completed work a contradiction invalidated, which is not
    /// recoverable from flags after the fact.
    /// </remarks>
    public IReadOnlyList<string> DroppedNodeIds { get; init; } = [];

    /// <summary>
    /// Why the previous version stopped being right. The thing a status flag destroys.
    /// </summary>
    /// <remarks>
    /// <b>Observed, not concluded.</b> It is taken from whatever stopped the plan — a node's own
    /// words when it disputed, the recorded count when it ran out of attempts, the failure's own
    /// message when it threw. Whoever decided what to do about it may add a
    /// <see cref="Rationale"/>; it does not get to write this.
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>
    /// Whoever decided the change of plan, and their argument for it. <see langword="null"/> when
    /// nobody offered one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Reason"/>, and separately attributed, because a reader must always be
    /// able to tell what was observed from what was inferred. Collapsing them is how a lineage comes
    /// to read like a record while being partly a guess.
    /// </remarks>
    public PlanRationale? Rationale { get; init; }
}

/// <summary>
/// A plan and every version it has been through: the durable structure a run reads from and writes
/// its verdicts back into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing crosses a node boundary.</b> A node does not receive a record from its sibling or hand
/// one to its parent; the tree holds each node's contract, its verdicts and its reported
/// contradictions, and a node reads it. That is the difference between a loss that is recoverable
/// and one that is not: what a handoff failed to carry was gone, while what a node failed to read is
/// still sitting there.
/// </para>
/// <para>
/// <b>The lineage is linear.</b> Execution is sequential — one node at a time, siblings in order —
/// so there is one writer, no divergent versions and no merge. That is a deliberate approximation,
/// and the condition that would force revisiting it is genuine fan-out over <em>provably disjoint</em>
/// areas, not a shortage of capacity.
/// </para>
/// <para>
/// <b>Versions are minted on re-ruling only.</b> Recording a verdict does not mint one; changing what
/// a node is asked to achieve does. Without that gate the lineage becomes churn and stops being
/// legible, which is the opposite of what it is for.
/// </para>
/// </remarks>
public sealed record PlanTree
{
    /// <summary>Identifier for this plan, stable across versions.</summary>
    public required string PlanId { get; init; }

    /// <summary>Every version, oldest first. Linear by construction.</summary>
    public required IReadOnlyList<PlanVersion> Lineage { get; init; }

    /// <summary>
    /// What has been settled about this plan and outlives the halts that settled it.
    /// </summary>
    /// <remarks>
    /// <b>Beside the lineage, not inside a version</b>, because a term is not something a version
    /// changed — it is a standing fact that every version after it has to be authored around.
    /// <see cref="Rerule"/> binds the ones in force into whatever contract it writes, which is the
    /// whole of *recall proposes; the contract binds*: this list is what is remembered, and a
    /// constraint on a contract is what actually holds a step to it.
    /// </remarks>
    public IReadOnlyList<PlanTerm> Terms { get; init; } = [];

    /// <summary>The version in force. Derived from the lineage, so never serialized.</summary>
    [JsonIgnore]
    public PlanVersion Current => Lineage[^1];

    /// <summary>The root node. Derived, so never serialized.</summary>
    [JsonIgnore]
    public PlanNode Root => Current.Nodes[Current.RootId];

    /// <summary>
    /// A work item and the work it decomposes into, to any depth. Used to describe a plan before it
    /// starts running.
    /// </summary>
    /// <param name="Id">Identifier, unique within the plan.</param>
    /// <param name="Contract">What this item is for.</param>
    /// <param name="Children">Its decomposition, in execution order.</param>
    public readonly record struct PlanNodeSpec(
        string Id,
        AgentContract Contract,
        IReadOnlyList<PlanNodeSpec>? Children = null);

    /// <summary>
    /// Creates a plan of any depth. Everything described here is version one.
    /// </summary>
    /// <remarks>
    /// Describing a plan is not the same as changing one. Building a tree by re-issuing each level
    /// would mint a version per level and leave a lineage full of entries that record nothing
    /// happening — which is exactly the churn that makes a lineage stop being worth reading.
    /// </remarks>
    public static PlanTree Create(
        string planId,
        AgentContract rootContract,
        IEnumerable<PlanNodeSpec> children,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(rootContract);
        ArgumentNullException.ThrowIfNull(children);

        var nodes = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        var rootChildIds = new List<string>();

        foreach (var child in children)
        {
            Flatten(child, planId, nodes);
            rootChildIds.Add(child.Id);
        }

        nodes[planId] = new PlanNode { Id = planId, Contract = rootContract, ChildIds = rootChildIds };

        return new PlanTree
        {
            PlanId = planId,
            Lineage =
            [
                new PlanVersion
                {
                    Number = 1,
                    RootId = planId,
                    Nodes = nodes,
                    MintedAt = (timeProvider ?? TimeProvider.System).GetUtcNow()
                }
            ]
        };
    }

    private static void Flatten(PlanNodeSpec spec, string parentId, Dictionary<string, PlanNode> nodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Id);
        ArgumentNullException.ThrowIfNull(spec.Contract);

        var childIds = new List<string>();
        foreach (var child in spec.Children ?? [])
        {
            Flatten(child, spec.Id, nodes);
            childIds.Add(child.Id);
        }

        if (!nodes.TryAdd(spec.Id, new PlanNode
        {
            Id = spec.Id,
            ParentId = parentId,
            Contract = spec.Contract,
            ChildIds = childIds
        }))
        {
            throw new ArgumentException($"Duplicate node id '{spec.Id}'.", nameof(spec));
        }
    }

    /// <summary>Creates a plan from a root contract and one level of children, in execution order.</summary>
    public static PlanTree Create(
        string planId,
        AgentContract rootContract,
        IEnumerable<(string Id, AgentContract Contract)> children,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(rootContract);
        ArgumentNullException.ThrowIfNull(children);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var rootId = planId;
        var nodes = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        var childIds = new List<string>();

        foreach (var (id, contract) in children)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(contract);

            if (!nodes.TryAdd(id, new PlanNode { Id = id, ParentId = rootId, Contract = contract }))
                throw new ArgumentException($"Duplicate node id '{id}'.", nameof(children));

            childIds.Add(id);
        }

        nodes[rootId] = new PlanNode { Id = rootId, Contract = rootContract, ChildIds = childIds };

        return new PlanTree
        {
            PlanId = planId,
            Lineage =
            [
                new PlanVersion
                {
                    Number = 1,
                    RootId = rootId,
                    Nodes = nodes,
                    MintedAt = now
                }
            ]
        };
    }

    /// <summary>The node with <paramref name="nodeId"/>.</summary>
    public PlanNode Node(string nodeId) => Current.Nodes[nodeId];

    /// <summary>Ancestors of <paramref name="nodeId"/>, nearest parent first.</summary>
    public IReadOnlyList<PlanNode> Ancestors(string nodeId)
    {
        var chain = new List<PlanNode>();
        var current = Current.Nodes[nodeId].ParentId;

        while (current is not null && Current.Nodes.TryGetValue(current, out var parent))
        {
            chain.Add(parent);
            current = parent.ParentId;
        }

        return chain;
    }

    /// <summary>
    /// Records something settled about the plan that outlives the halt it was settled at. Mints no
    /// version.
    /// </summary>
    /// <remarks>
    /// <b>No version, because the plan did not change — what changed is what any later change has to
    /// respect.</b> It takes force at the next <see cref="Rerule"/>, which binds it into the contract
    /// it writes. Committing one re-words no criterion that already exists, so a step already under
    /// way is not retrospectively held to a term nobody had when it started.
    /// </remarks>
    public PlanTree WithTerm(PlanTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return this with { Terms = [.. Terms, term] };
    }

    /// <summary>Revokes a term, recording why. Mints no version, and removes nothing.</summary>
    /// <remarks>
    /// <b>Revoked rather than deleted, for the same reason a dropped node stays in its old version.</b>
    /// That a preference was in force and then was not is a fact about the run, and the reason it
    /// stopped is the part nothing could reconstruct from the plan afterwards. Contracts already
    /// written keep it: a term binds what is authored while it holds, and un-writing a constraint out
    /// of a live contract would change work already under way on nobody's decision.
    /// </remarks>
    public PlanTree Contradict(
        string termId, string reason, PlanTermEnd end, string by, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(termId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var revised = Terms.ToList();
        var found = revised.FindIndex(t => string.Equals(t.Id, termId, StringComparison.Ordinal));

        if (found < 0)
            throw new InvalidOperationException(
                $"No term '{termId}' on plan '{PlanId}', so there is nothing to contradict.");

        revised[found] = revised[found] with
        {
            Contradicted = reason,
            ContradictedAt = at,
            End = end,
            EndedBy = by
        };

        return this with { Terms = revised };
    }

    /// <summary>
    /// The contract with every term in force added to its constraints, and nothing said twice.
    /// </summary>
    /// <remarks>
    /// <b>Where a term stops being recall and starts binding.</b> Applied at the one choke point every
    /// change of plan goes through, so it holds whoever authored the replacement and however many
    /// versions later — which is the whole of what the story asks for: two versions on, the thing
    /// somebody settled is still not back on the table.
    /// </remarks>
    private AgentContract Bind(AgentContract contract)
    {
        var merged = new List<string>(contract.Constraints);

        foreach (var term in Terms)
        {
            if (term.InForce && !merged.Contains(term.Reading, StringComparer.Ordinal))
                merged.Add(term.Reading);
        }

        return merged.Count == contract.Constraints.Count
            ? contract
            : contract with { Constraints = merged };
    }

    /// <summary>
    /// Records a verdict against a node's criterion. **Does not mint a version** — a verdict is a
    /// fact about the work, not a change to what the work is.
    /// </summary>
    public PlanTree WithVerdict(string nodeId, CriterionVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var node = Current.Nodes[nodeId];
        return Replace(node with { Verdicts = [.. node.Verdicts, verdict] });
    }

    /// <summary>
    /// Records a node's report that its contract is wrong. Also does not mint a version: whether the
    /// plan changes is for whoever authored the criterion to decide.
    /// </summary>
    public PlanTree WithViolation(string nodeId, PlanViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        return Replace(Current.Nodes[nodeId] with { Violation = violation });
    }

    /// <summary>
    /// Removes a node's reported contradiction, after something ruled the node was mistaken about it.
    /// </summary>
    /// <remarks>
    /// Deliberately not something a re-run does on its own: a report does not evaporate because
    /// someone ran the node again. It goes when a check disagreed with it, or when the contract it
    /// disputed is re-ruled.
    /// </remarks>
    public PlanTree ClearViolation(string nodeId) =>
        Replace(Current.Nodes[nodeId] with { Violation = null });

    /// <summary>Records that a node is waiting to be told something. Mints no version.</summary>
    public PlanTree WithQuestion(string nodeId, NodeQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return Replace(Current.Nodes[nodeId] with { Question = question });
    }

    /// <summary>Records what a node was told, and clears what it was waiting for.</summary>
    /// <remarks>
    /// <b>The question goes and the answer stays.</b> A node still carrying the question it has been
    /// answered would halt the next pass on a choice somebody already made — and the answer has to
    /// outlive the question, because it is the only thing the next attempt can read.
    /// </remarks>
    public PlanTree WithAnswer(string nodeId, PlanAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var node = Current.Nodes[nodeId];
        return Replace(node with { Question = null, Answers = [.. node.Answers, answer] });
    }

    /// <summary>Removes what a node was waiting to be told, because a later attempt replaced it.</summary>
    public PlanTree ClearQuestion(string nodeId) =>
        Current.Nodes[nodeId].Question is null
            ? this
            : Replace(Current.Nodes[nodeId] with { Question = null });

    /// <summary>Marks where a step stands, and the option it settled on. Mints no version.</summary>
    public PlanTree WithState(string nodeId, StepState state, string? result = null) =>
        Current.Nodes[nodeId] is var node && node.State == state && node.Result == result
            ? this
            : Replace(node with { State = state, Result = result });

    /// <summary>Where the plan stands.</summary>
    /// <remarks>
    /// <see cref="StepState.Done"/> when nothing in it needs another attempt, <see cref="StepState.Skipped"/>
    /// when it was given up, <see cref="StepState.Blocked"/> while any step is, otherwise
    /// <see cref="StepState.Pending"/>.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public StepState State =>
        LifecycleOf(Current.RootId) is NodeLifecycle.Abandoned ? StepState.Skipped
        : Settled(Current.RootId) ? StepState.Done
        : Current.Nodes.Values.Any(n => n.State is StepState.Blocked) ? StepState.Blocked
        : StepState.Pending;

    /// <summary>Records that this work is given up. Mints no version, and deletes nothing.</summary>
    /// <remarks>
    /// <b>A state on the node, never an edit to anybody's child list.</b> The node stays where it is,
    /// carrying who gave it up and why, so <em>what happened to this?</em> is answered by looking at it
    /// rather than by diffing two versions to find what is missing from one of them.
    /// </remarks>
    public PlanTree Abandon(string nodeId, PlanAbandonment abandonment)
    {
        ArgumentNullException.ThrowIfNull(abandonment);

        return Replace(Current.Nodes[nodeId] with { Abandonment = abandonment, State = StepState.Skipped });
    }

    /// <summary>Records that an attempt at a node did not finish. Mints no version.</summary>
    public PlanTree WithFailure(string nodeId, NodeFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return Replace(Current.Nodes[nodeId] with { Failure = failure });
    }

    /// <summary>
    /// Removes a recorded failure, because a later attempt ran to completion.
    /// </summary>
    /// <remarks>
    /// <b>Unlike a dispute, which a re-run does not clear.</b> A dispute is a claim about the
    /// contract and survives until the contract changes; a failure is a claim about one attempt, and
    /// an attempt that finished is the direct answer to it. Leaving it would make a node that
    /// recovered read as broken for the rest of the run.
    /// </remarks>
    public PlanTree ClearFailure(string nodeId) =>
        Current.Nodes[nodeId].Failure is null
            ? this
            : Replace(Current.Nodes[nodeId] with { Failure = null });

    /// <summary>Marks a node as being attempted right now. Mints no version.</summary>
    /// <remarks>
    /// Written before the attempt rather than after it, which is the whole of its use: what a reader
    /// wants after a long pause is which step is in flight, and that is unanswerable from anything
    /// the attempt will produce later.
    /// </remarks>
    public PlanTree WithAttempt(string nodeId, DateTimeOffset at) =>
        Replace(Current.Nodes[nodeId] with { AttemptStartedAt = at });

    /// <summary>Clears a node's in-flight mark, because its attempt is over however it ended.</summary>
    public PlanTree ClearAttempt(string nodeId) =>
        Current.Nodes[nodeId].AttemptStartedAt is null
            ? this
            : Replace(Current.Nodes[nodeId] with { AttemptStartedAt = null });

    /// <summary>
    /// Clears every in-flight mark, because a pass that is starting has nothing running yet.
    /// </summary>
    /// <remarks>
    /// <b>The reconciliation a stored fact has to come with.</b> A run killed mid-step leaves a mark
    /// nothing will ever clear, and a node that reads <c>Running</c> forever is worse than one that
    /// never reported it — so the next pass corrects it rather than inheriting it. Safe because the
    /// executor is sequential: when a pass begins, nothing else is attempting anything.
    /// </remarks>
    public PlanTree ClearAttempts()
    {
        var running = Current.Nodes.Values.Where(n => n.AttemptStartedAt is not null).ToList();

        if (running.Count == 0)
            return this;

        var tree = this;
        foreach (var node in running)
            tree = tree.Replace(node with { AttemptStartedAt = null });

        return tree;
    }

    /// <summary>
    /// Records that a node ran and read the tree. Like a verdict, this mints no version — it is a
    /// fact about the work, not a change to it.
    /// </summary>
    public PlanTree WithReading(string nodeId, NodeReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var node = Current.Nodes[nodeId];
        return Replace(node with { LastRead = reading, ReadCount = node.ReadCount + 1 });
    }

    /// <summary>
    /// Whether a node was last checked against a plan version that is no longer the one in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drift question, and it is deliberately separate from status. Re-ruling clears verdicts in
    /// the subtree it invalidates and <em>nowhere else</em> — correctly, because contracts flow
    /// downward rather than sideways. So a node can stay legitimately satisfied while the plan
    /// around it has moved on, and nothing about its verdicts would say so.
    /// </para>
    /// <para>
    /// A node that has never run is <b>not</b> stale — it is planned, which
    /// <see cref="LifecycleOf"/> already reports. Conflating the two would turn every fresh plan into
    /// a wall of warnings.
    /// </para>
    /// </remarks>
    public bool IsStale(string nodeId) =>
        Current.Nodes[nodeId].LastRead is { } reading && reading.PlanVersion < Current.Number;

    /// <summary>
    /// Every node whose last run predates the plan version now in force, in no particular order.
    /// </summary>
    /// <remarks>
    /// Derived on demand rather than recorded anywhere, for the same reason a node carries no status
    /// field: a stored answer to a question the structure can answer is a second source of truth, and
    /// it is wrong from the moment anything re-rules. Ask the tree; the tree always knows.
    /// </remarks>
    public IReadOnlyList<string> StaleNodes() =>
        [.. Current.Nodes.Keys.Where(IsStale)];

    /// <summary>
    /// Re-rules a node's contract and mints a new plan version, recording why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unit is the subtree rooted at the node that re-ruled: contracts flow downward, so
    /// re-ruling invalidates what was issued beneath it and nothing else. Everything outside that
    /// subtree is carried over by reference, unchanged.
    /// </para>
    /// <para>
    /// <b>A redesign may change the decomposition, not only the wording.</b> Supplying
    /// <paramref name="children"/> replaces the node's children: ones not listed are dropped from
    /// this version — recorded in <see cref="PlanVersion.DroppedNodeIds"/> and still readable in
    /// every earlier version — and ones not present before are added. A re-ruling that could only
    /// reword a contract would not be able to express the most common outcome of discovering a plan
    /// is wrong, which is that some of the planned work is no longer the work.
    /// </para>
    /// <para>
    /// <b>Only verdicts under a contract that actually changed are cleared.</b> A node whose own
    /// contract is identical in the new version was not re-issued anything different; its verdict
    /// was a fact about criteria that still stand, decided by an oracle that still would. What the
    /// re-ruling changes for such a node is whether its work is still <em>wanted</em> — and that is
    /// said by whether it is still in the tree, not by destroying the record that it was done.
    /// </para>
    /// <para>
    /// <b><paramref name="reason"/> is what was observed, not what anyone concluded.</b> From a halt
    /// it is <c>PlanCoordination.HaltReason</c> — the disputing node's own words, the recorded
    /// attempt count, or the failure's own message. Whoever decided the change may add a
    /// <paramref name="rationale"/> beside it, attributed; the two are never merged, because a reader
    /// has to be able to tell a record from a guess.
    /// </para>
    /// </remarks>
    public PlanTree Rerule(
        string nodeId,
        AgentContract contract,
        string reason,
        IEnumerable<(string Id, AgentContract Contract)>? children = null,
        TimeProvider? timeProvider = null,
        PlanRationale? rationale = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var nodes = new Dictionary<string, PlanNode>(Current.Nodes, StringComparer.Ordinal);
        var target = nodes[nodeId];
        var dropped = new List<string>();

        // Its own contract changed by definition, so its verdicts, any dispute, any outstanding
        // question and any recorded failure all go — each is a claim about the old contract, and a
        // node handed different work has not attempted that work yet. The reading stays: it is the
        // record that the node ran at all, and leaving it is what makes the node visibly stale
        // afterwards rather than merely pending.
        nodes[nodeId] = target with
        {
            Contract = Bind(contract),
            Verdicts = [],
            Violation = null,
            Question = null,
            Failure = null,
            ReadCount = 0,
            State = target.State is StepState.Skipped ? StepState.Skipped : StepState.Pending,
            Result = null
        };

        if (children is not null)
        {
            var replacement = children.ToList();

            // A plan whose nodes contain themselves cannot be walked, and the walk is not defensive
            // about it: PostOrder recurses until the stack ends, which is a process death rather
            // than an exception — uncatchable, and with nothing said about why. This arrives from
            // exactly one place in practice, a model authoring a decomposition and naming an id that
            // is already above it, so it is refused here where the id is still attributable.
            var ancestry = Ancestry(nodeId);

            foreach (var (id, _) in replacement)
            {
                if (!ancestry.Contains(id))
                    continue;

                throw new ArgumentException(
                    $"'{id}' cannot become a child of '{nodeId}': it is "
                    + (string.Equals(id, nodeId, StringComparison.Ordinal)
                        ? "that node itself"
                        : $"an ancestor of it")
                    + ", and a plan whose nodes contain themselves cannot be walked.",
                    nameof(children));
            }

            var duplicate = replacement
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate is not null)
                throw new ArgumentException(
                    $"'{duplicate.Key}' is named twice among the children of '{nodeId}'.",
                    nameof(children));

            var keptIds = replacement.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var formerChild in target.ChildIds.Where(id => !keptIds.Contains(id)))
            {
                dropped.Add(formerChild);
                foreach (var deeper in Descendants(formerChild))
                    dropped.Add(deeper);
            }

            foreach (var goneId in dropped)
                nodes.Remove(goneId);

            foreach (var (id, childContract) in replacement)
            {
                var bound = Bind(childContract);

                nodes[id] = nodes.TryGetValue(id, out var existing)
                    ? Reissue(existing, bound)
                    : new PlanNode { Id = id, ParentId = nodeId, Contract = bound };
            }

            nodes[nodeId] = nodes[nodeId] with { ChildIds = [.. replacement.Select(c => c.Id)] };
        }
        else
        {
            foreach (var descendant in Descendants(nodeId))
                nodes[descendant] = Reissue(nodes[descendant], nodes[descendant].Contract);
        }

        return this with
        {
            Lineage =
            [
                .. Lineage,
                new PlanVersion
                {
                    Number = Current.Number + 1,
                    RootId = Current.RootId,
                    Nodes = nodes,
                    MintedAt = (timeProvider ?? TimeProvider.System).GetUtcNow(),
                    ReRuledNodeId = nodeId,
                    Reason = reason,
                    Rationale = rationale,
                    DroppedNodeIds = dropped
                }
            ]
        };
    }

    /// <summary>
    /// Re-issues a node under <paramref name="contract"/>, keeping its verdicts when nothing about
    /// what it was asked to do has changed.
    /// </summary>
    private static PlanNode Reissue(PlanNode node, AgentContract contract) =>
        Same(node.Contract, contract)
            ? node with { Contract = contract }
            : node with
            {
                Contract = contract,
                Verdicts = [],
                Violation = null,
                Question = null,
                Failure = null,
                ReadCount = 0,
                State = node.State is StepState.Skipped ? StepState.Skipped : StepState.Pending,
                Result = null
            };

    /// <summary>Whether two contracts ask for the same thing.</summary>
    /// <remarks>
    /// <b>By content, and the record's own <c>==</c> will not do it.</b> A record compares each member
    /// with <c>EqualityComparer&lt;T&gt;.Default</c>, and the members that matter here are
    /// <c>IReadOnlyList&lt;string&gt;</c> — compared by reference. So a contract re-authored
    /// character for character was <em>unequal</em> to the one it replaced, and re-listing an
    /// unchanged step silently threw its verdicts away and re-ran completed work. Found while
    /// planning the seat that re-authors child lists, which is the one thing that would have hit it
    /// on every run.
    /// </remarks>
    private static bool Same(AgentContract a, AgentContract b) =>
        string.Equals(a.Goal, b.Goal, StringComparison.Ordinal)
        && a.AcceptanceCriteria.SequenceEqual(b.AcceptanceCriteria, StringComparer.Ordinal)
        && a.Constraints.SequenceEqual(b.Constraints, StringComparer.Ordinal);

    /// <summary>A node and everything above it, which is what a child may not be.</summary>
    /// <remarks>
    /// Walks parents rather than searching, and stops if it ever revisits a node — so it terminates
    /// even on a tree that is already malformed, which is the state it exists to keep out.
    /// </remarks>
    private HashSet<string> Ancestry(string nodeId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var at = nodeId; at is not null && seen.Add(at);)
            at = Current.Nodes.TryGetValue(at, out var node) ? node.ParentId : null;

        return seen;
    }

    /// <summary>What has happened to the attempt on a node. Its own attempts, never its children's.</summary>
    /// <remarks>
    /// <b>Deliberately not aggregated upward.</b> A parent runs after its children, so "has this node
    /// been attempted" is a question about this node — and rolling children into it would silently
    /// answer a different one. What a plan is worth as a whole is <see cref="OutcomeOf"/>, which does
    /// climb, because a contract is not met while the contracts beneath it are not.
    /// </remarks>
    public NodeLifecycle LifecycleOf(string nodeId)
    {
        var node = Current.Nodes[nodeId];

        // First, because it is the last word: nothing will attempt this again, whatever it did before.
        if (node.Abandonment is not null)
            return NodeLifecycle.Abandoned;

        // Before the past tenses, because it is the only fact about now.
        if (node.AttemptStartedAt is not null)
            return NodeLifecycle.Running;

        // Before "it ran", because it is the more recent fact: a node that finished and later died
        // is a node whose last attempt died.
        if (node.Failure is not null)
            return NodeLifecycle.Faulted;

        return node.ReadCount == 0 ? NodeLifecycle.Planned : NodeLifecycle.Completed;
    }

    /// <summary>Derives what the verdicts say about a node's contract, and its children's.</summary>
    /// <remarks>
    /// <b>A dispute wins over the arithmetic and travels upward.</b> A node reporting that its contract
    /// is wrong is a claim about the plan, and a parent whose child says that has not met its own
    /// contract either — it is waiting on the same answer.
    /// </remarks>
    public ContractOutcome OutcomeOf(string nodeId)
    {
        var node = Current.Nodes[nodeId];

        if (node.Violation is not null)
            return ContractOutcome.Disputed;

        // Abandoned children are not waited on. Their own outcome stays Unmet — the contract genuinely
        // is not met, and the criteria remain the record — but a parent does not stay unmet because of
        // work somebody decided not to do.
        var childOutcomes = node.ChildIds
            .Where(id => Current.Nodes[id].Abandonment is null)
            .Select(OutcomeOf)
            .ToList();

        if (childOutcomes.Any(o => o is ContractOutcome.Disputed))
            return ContractOutcome.Disputed;

        // The latest verdict per criterion is the one that counts. Verdicts accumulate rather than
        // overwrite — that history is the evidence a fix worked — but a criterion that failed, was
        // fixed and now passes is passing. Reading *any* failing verdict as unmet would leave a node
        // permanently unmet by a result that has since been superseded, which is the same mistake as
        // discarding an old record instead of re-running the check that produced it.
        var latest = node.LatestVerdicts;

        if (latest.Any(v => !v.Passed))
            return ContractOutcome.Unmet;

        var passed = latest.Where(v => v.Passed).Select(v => v.Criterion).ToHashSet(StringComparer.Ordinal);
        // A step the loop marked Done settled on a result, and no verdict above says otherwise.
        var criteriaMet = node.State is StepState.Done
            || node.Contract.AcceptanceCriteria.Count == 0
            || node.Contract.AcceptanceCriteria.All(passed.Contains);

        return criteriaMet && childOutcomes.All(o => o is ContractOutcome.Met)
            ? ContractOutcome.Met
            : ContractOutcome.Unmet;
    }

    /// <summary>Whether a node needs no further attempt.</summary>
    /// <remarks>
    /// <b>Two ways to need none, and they are not the same news.</b> Either the contract holds, or the
    /// work was given up — and a reader wants to know which, which is what the lifecycle is for.
    /// <para>
    /// The "and its last attempt did not die" half is not redundant. A node whose criteria all hold and
    /// whose last attempt threw is work somebody should look at again: the verdicts describe what was
    /// true before something went wrong, and skipping it would report a run as finished on the strength
    /// of evidence that predates its own failure.
    /// </para>
    /// </remarks>
    public bool Settled(string nodeId) =>
        LifecycleOf(nodeId) switch
        {
            NodeLifecycle.Abandoned => true,
            NodeLifecycle.Faulted => false,
            _ => Current.Nodes[nodeId].State is StepState.Done || OutcomeOf(nodeId) is ContractOutcome.Met
        };

    private IEnumerable<string> Descendants(string nodeId)
    {
        foreach (var childId in Current.Nodes[nodeId].ChildIds)
        {
            yield return childId;
            foreach (var deeper in Descendants(childId))
                yield return deeper;
        }
    }

    private PlanTree Replace(PlanNode node)
    {
        var nodes = new Dictionary<string, PlanNode>(Current.Nodes, StringComparer.Ordinal)
        {
            [node.Id] = node
        };

        // Amending the version in place rather than minting a new one: this is a fact recorded
        // about the work, and minting here would bury the re-rulings in churn.
        return this with
        {
            Lineage = [.. Lineage.Take(Lineage.Count - 1), Current with { Nodes = nodes }]
        };
    }
}
