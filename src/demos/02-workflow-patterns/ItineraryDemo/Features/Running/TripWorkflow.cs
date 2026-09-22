using Ananke.Orchestration;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using ItineraryDemo.Features.Planning;
using ItineraryDemo.Features.Stays;
using ItineraryDemo.Shared;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Running;

/// <summary>Workflow state: how the last pass ended, and what was decided about it.</summary>
internal sealed record Trip
{
    public PlanCoordination? Coordination { get; init; }
}

/// <summary>
/// The scenario assembled — who does a step, who rules on it, and who decides what a halt means.
/// </summary>
/// <remarks>
/// <para>
/// <b>One topology, whoever is watching.</b> The same jobs over the same edges run attended and
/// unattended: the plan halts, the supervisor is asked what the halt admits, somebody picks, and the
/// work resumes under the new version. What the flags choose is <em>who</em> answers.
/// </para>
/// <para>
/// <b>A step reports; the loop marks it.</b> A step searches the service and reports whether it found
/// what its contract asks for, with every stay it found. One stay that fits and the step is done with
/// it; several go to the supervisor to choose; none that fit and the Planner changes the plan.
/// </para>
/// </remarks>
internal static class TripWorkflow
{
    /// <summary>
    /// How many times this run may change its plan before it stops trying.
    /// </summary>
    /// <remarks>
    /// <b>Set here because nothing invents one.</b> What a run costs is bounded by its budget and
    /// what a step may attempt by its contract; how much re-planning is worth doing is a third
    /// judgement, and it belongs to whoever is paying.
    /// </remarks>
    public const int ChangesAllowed = 3;

    public static Workflow<Trip> Build(
        TripService service,
        IPlanTreeStore store,
        TripModels models,
        PlanTree plan,
        InvocationCheck validate,
        bool autopilot,
        Narration narration)
    {
        var executor = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = models.Executor,
            Persona = "You find stays. Search with your tools and report the stays the service has. "
                + "You do not choose between them.",
            ConfigureFor = (job, _) => job
                .WithTools(new StayTools(service).Search())
                .WithMaxToolRounds(4)
        });

        var supervision = new SupervisionOptions
        {
            Runner = PlanNodeRunners.LeavesOnly(executor.AsRunner()),
            Verifier = new DeterministicVerifier([validate]),
            Checks = [validate],
            Store = store,
            Supervisor = models.Supervisor,
            Planner = models.Supervisor
        };

        var advisor = new AgentPlanAdvisor(supervision).AsAdvisor();

        var planner = new TripPlanner(
            supervision.ModelFor(PlanRoles.Planner)!, service, validate, narration).AsAuthor();

        return AgenticPattern.SupervisedPlan<Trip>(plan.PlanId)
            .WithPlan(_ => plan)
            .Supervised(supervision)
            .Tracking(state => state.Coordination, (state, coordination) => state with { Coordination = coordination })
            .WithAdvisor(advisor, unattended: autopilot)
            .WithPlanner(planner)
            .MaxChangesOfPlan(ChangesAllowed)
            .Build()
            .UseCheckpointing(new InMemoryCheckpointStore());
    }
}
