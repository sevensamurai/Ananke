using System.ComponentModel;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The two shipped <see cref="IDeterministicCheck"/> implementations: a predicate, and a command's
/// exit code.
/// </summary>
/// <remarks>
/// <para>
/// Both exist because reaching a gate at all used to mean writing a class first, and the gate is the
/// part of verification that must never be skipped. What is under test is mostly what they
/// <em>refuse</em> to do: neither answers for a criterion it was not given, because a check that
/// claims everything makes abstention impossible, and abstention is what keeps the unverified
/// visible.
/// </para>
/// <para>
/// <see cref="ProcessCheck"/> has one distinction the interface cannot express: <b>"the check could
/// not run" is not "the criterion is not met"</b>. A missing executable and a command that outruns
/// its timeout throw rather than returning <see langword="false"/>, because a fabricated verdict in
/// the tree carries the authority of something that ran.
/// </para>
/// </remarks>
[TestFixture]
public class DeterministicCheckTests
{
    private const string Gate = "the build is green";
    private const string Other = "the docs mention it";

    // ── PredicateCheck ──

    [Test]
    public void CanRule_ACriterionItWasGiven_IsTrue() =>
        new PredicateCheck("a check", [Gate], _ => true).CanRule(Gate).ShouldBeTrue();

    [Test]
    public void CanRule_ACriterionItWasNotGiven_IsFalse() =>
        new PredicateCheck("a check", [Gate], _ => true).CanRule(Other).ShouldBeFalse();

    [Test]
    public async Task RunAsync_APassingPredicate_Passes() =>
        (await new PredicateCheck("a check", [Gate], _ => true).RunAsync(Gate)).Holds.ShouldBeTrue();

    [Test]
    public async Task RunAsync_AFailingPredicate_Fails() =>
        (await new PredicateCheck("a check", [Gate], _ => false).RunAsync(Gate)).Holds.ShouldBeFalse();

    [Test]
    public async Task RunAsync_AFailingPredicate_HasNoDetail()
    {
        // A predicate only ever answers true or false; there is no "why" it can offer that is not
        // already in the criterion's own text, so it must not invent one.
        var finding = await new PredicateCheck("a check", [Gate], _ => false).RunAsync(Gate);

        finding.Detail.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_ManyCriteria_PassesTheCriterionToThePredicate()
    {
        // One predicate, several criteria: it has to be told which one it is deciding, or the
        // second criterion silently gets the first one's answer.
        var check = new PredicateCheck("a check", [Gate, Other], c => c == Gate);

        (await check.RunAsync(Gate)).Holds.ShouldBeTrue();
        (await check.RunAsync(Other)).Holds.ShouldBeFalse();
    }

    [Test]
    public async Task RunAsync_AnAsynchronousPredicate_IsAwaitedAndGetsTheToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        var check = new PredicateCheck("a check", [Gate], async (_, ct) =>
        {
            seen = ct;
            await Task.Yield();
            return true;
        });

        (await check.RunAsync(Gate, cts.Token)).Holds.ShouldBeTrue();
        seen.ShouldBe(cts.Token);
    }

    [Test]
    public async Task RunAsync_ACriterionItCannotRule_Throws()
    {
        // Answering here would be a verdict on something nobody said this check covers.
        var check = new PredicateCheck("a check", [Gate], _ => true);

        await Should.ThrowAsync<InvalidOperationException>(() => check.RunAsync(Other));
    }

    [Test]
    public async Task Verify_AGateThisCheckDoesNotCover_Abstains()
    {
        var ruling = await Rule([Gate, Other], new PredicateCheck("a check", [Gate], _ => true));

        ruling.Outcome.ShouldBe(VerificationOutcome.Abstained);
        ruling.Abstained.ShouldHaveSingleItem().ShouldBe(Other);
        ruling.Verdicts.ShouldHaveSingleItem().Oracle.ShouldBe("a check");
    }

    // ── ProcessCheck ──

    [Test]
    public void Oracle_ACommandWithArguments_NamesHowToRunItAgain() =>
        new ProcessCheck("dotnet", "test --no-build", [Gate]).Oracle.ShouldBe("dotnet test --no-build");

    [Test]
    public void Oracle_ACommandWithNoArguments_IsTheCommand() =>
        new ProcessCheck("make", "", [Gate]).Oracle.ShouldBe("make");

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatExitsZero_Passes(CancellationToken ct) =>
        (await Exits(0, [Gate]).RunAsync(Gate, ct)).Holds.ShouldBeTrue();

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatExitsNonZero_Fails(CancellationToken ct) =>
        (await Exits(1, [Gate]).RunAsync(Gate, ct)).Holds.ShouldBeFalse();

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatExitsZero_HasNoDetail(CancellationToken ct)
    {
        // A check that held needs nothing explained.
        var finding = await Exits(0, [Gate]).RunAsync(Gate, ct);

        finding.Detail.ShouldBeNull();
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatFails_CarriesWhatItPrinted(CancellationToken ct)
    {
        var (file, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c echo something broke 1>&2 & exit 1")
            : ("/bin/sh", "-c \"echo something broke >&2; exit 1\"");

        var check = new ProcessCheck(file, args, [Gate]);
        var finding = await check.RunAsync(Gate, ct);

        finding.Holds.ShouldBeFalse();
        finding.Detail.ShouldNotBeNull().ShouldContain("something broke");
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_OutputLongerThanTheCap_IsTruncatedRatherThanBuffered(CancellationToken ct)
    {
        // The cap exists so one runaway process cannot hold megabytes of console output in memory —
        // it is a safety valve, not a real budget, and says so in its own doc comment.
        var (file, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c for /L %i in (1,1,20000) do @echo 0123456789 & exit 1")
            : ("/bin/sh", "-c \"yes 0123456789 | head -c 200000; exit 1\"");

        var check = new ProcessCheck(file, args, [Gate]);
        var finding = await check.RunAsync(Gate, ct);

        finding.Holds.ShouldBeFalse();
        finding.Detail.ShouldNotBeNull().Length.ShouldBeLessThan(50_000);
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ADeclaredSuccessExitCode_Passes(CancellationToken ct)
    {
        // Not every runner reports success as 0 — a tool that returns 2 for "nothing to do" is
        // still a passing gate if the criterion says so.
        var (file, args) = Exit(2);
        var check = new ProcessCheck(new ProcessCheckOptions
        {
            FileName = file,
            Arguments = args,
            Criteria = [Gate],
            SuccessExitCodes = [0, 2]
        });

        (await check.RunAsync(Gate, ct)).Holds.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACriterionItCannotRule_Throws(CancellationToken ct) =>
        await Should.ThrowAsync<InvalidOperationException>(() => Exits(0, [Gate]).RunAsync(Other, ct));

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatDoesNotExist_ThrowsRatherThanFailingTheGate(
        CancellationToken ct)
    {
        // The distinction the interface cannot make: a broken oracle decided nothing, and must not
        // be recorded as a criterion that was checked and did not hold.
        var check = new ProcessCheck("ananke-no-such-command-8f3a", "", [Gate]);

        await Should.ThrowAsync<Win32Exception>(() => check.RunAsync(Gate, ct));
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACommandThatOutrunsItsTimeout_ThrowsRatherThanFailingTheGate(
        CancellationToken ct)
    {
        var (file, args) = Sleep(30);
        var check = new ProcessCheck(new ProcessCheckOptions
        {
            FileName = file,
            Arguments = args,
            Criteria = [Gate],
            Timeout = TimeSpan.FromMilliseconds(250)
        });

        await Should.ThrowAsync<TimeoutException>(() => check.RunAsync(Gate, ct));
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task RunAsync_ACancelledRun_DoesNotReportAVerdict()
    {
        var (file, args) = Sleep(30);
        var check = new ProcessCheck(new ProcessCheckOptions
        {
            FileName = file,
            Arguments = args,
            Criteria = [Gate]
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Should.ThrowAsync<OperationCanceledException>(() => check.RunAsync(Gate, cts.Token));
    }

    [Test]
    public void Construct_NoSuccessExitCode_Throws() =>
        Should.Throw<ArgumentException>(() => new ProcessCheck(new ProcessCheckOptions
        {
            FileName = "make",
            Criteria = [Gate],
            SuccessExitCodes = []
        }));

    [Test]
    [CancelAfter(30_000)]
    public async Task Verify_AProcessGate_RecordsTheCommandAsTheOracle(CancellationToken ct)
    {
        var ruling = await Rule([Gate], Exits(0, [Gate]), ct);

        ruling.Outcome.ShouldBe(VerificationOutcome.Passed);
        ruling.Verdicts.ShouldHaveSingleItem().Oracle.ShouldBe(Exits(0, [Gate]).Oracle);
    }

    // ── Helpers ──

    private static Task<Verification> Rule(
        string[] gates, IDeterministicCheck check, CancellationToken ct = default) =>
        new DeterministicVerifier([check]).VerifyAsync(new VerificationRequest
        {
            Node = new PlanNode
            {
                Id = "n",
                Contract = new AgentContract { Goal = "Do the work", AcceptanceCriteria = gates }
            },
            TreeView = string.Empty
        }, ct);

    private static ProcessCheck Exits(int code, string[] criteria)
    {
        var (file, args) = Exit(code);
        return new ProcessCheck(file, args, criteria);
    }

    // The reference platform is Linux and CI runs ubuntu-latest; the Windows arm keeps these
    // runnable from a developer's own machine.
    private static (string File, string Arguments) Exit(int code) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c exit {code}")
            : ("/bin/sh", $"-c \"exit {code}\"");

    private static (string File, string Arguments) Sleep(int seconds) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c ping -n {seconds} 127.0.0.1")
            : ("/bin/sh", $"-c \"sleep {seconds}\"");
}
