using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Middleware;
using Shouldly;

namespace Ananke.Orchestration.Tests;

public class LoggingMiddleware : IJobMiddleware<object>
{
    public List<string> Log { get; } = [];

    public async Task<object> InvokeAsync(
        string jobName,
        object state,
        Func<Task<object>> next,
        CancellationToken ct = default)
    {
        Log.Add($"before:{jobName}");
        var result = await next();
        Log.Add($"after:{jobName}");
        return result;
    }
}

[TestFixture]
public class MiddlewareTests
{
    [Test]
    public async Task Middleware_ExecutesAroundEachJob()
    {
        var middleware = new LoggingMiddleware();
        var runner = new WorkflowRunner(middlewares: [middleware]);

        var execution = await new Workflow<CounterState>("with-middleware")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Chain("a", "b", Workflow.End)
            .UseRunner(runner)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(2);

        middleware.Log.ShouldBe(new[]
        {
            "before:a", "after:a",
            "before:b", "after:b"
        });
    }

    [Test]
    public async Task MultipleMiddlewares_ExecuteInOrder()
    {
        var log = new List<string>();

        var first = new OrderedMiddleware("first", log);
        var second = new OrderedMiddleware("second", log);
        var runner = new WorkflowRunner(middlewares: [first, second]);

        var execution = await new Workflow<CounterState>("multi-middleware")
            .Job("work", (s, _) =>
            {
                log.Add("execute");
                return Task.FromResult(s with { Value = 1 });
            })
            .Then("work", Workflow.End)
            .UseRunner(runner)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);

        // First registered middleware wraps second, which wraps the job
        log.ShouldBe(new[]
        {
            "before:first",
            "before:second",
            "execute",
            "after:second",
            "after:first"
        });
    }
}

file class OrderedMiddleware(string name, List<string> log) : IJobMiddleware<object>
{
    public async Task<object> InvokeAsync(
        string jobName,
        object state,
        Func<Task<object>> next,
        CancellationToken ct = default)
    {
        log.Add($"before:{name}");
        var result = await next();
        log.Add($"after:{name}");
        return result;
    }
}
