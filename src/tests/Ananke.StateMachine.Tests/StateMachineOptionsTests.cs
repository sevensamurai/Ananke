using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class StateMachineOptionsTests
{
    [Test]
    public void Defaults_AllowImplicitSelfTransitions_True()
    {
        var options = new StateMachineOptions();

        options.AllowImplicitSelfTransitions.ShouldBeTrue();
    }

    [Test]
    public void Defaults_LockRetryCount_Three()
    {
        var options = new StateMachineOptions();

        options.LockRetryCount.ShouldBe(3);
    }

    [Test]
    public void Defaults_LockRetryDelayMs_Hundred()
    {
        var options = new StateMachineOptions();

        options.LockRetryDelayMs.ShouldBe(100);
    }
}
