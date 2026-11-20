using System.Reflection;
using StackExchange.Redis;

namespace Ananke.Redis.Tests;

/// <summary>
/// An <see cref="IConnectionMultiplexer"/> that sends nothing: every call on it, on the database it hands out and on
/// the transactions that database creates is recorded as <c>"{target}.{method}"</c> in <see cref="Calls"/>, with
/// target <c>multiplexer</c>, <c>database</c> or <c>transaction</c>. Commands answer the default of their result
/// type; <see cref="ITransaction.ExecuteAsync"/> answers <see langword="true"/>.
/// </summary>
internal sealed class RecordingMultiplexer
{
    public List<string> Calls { get; } = [];

    public IConnectionMultiplexer Connection { get; }

    public RecordingMultiplexer()
    {
        var transaction = Recorder.Create<ITransaction>(this, "transaction", null);
        var database = Recorder.Create<IDatabase>(this, "database", transaction);
        Connection = Recorder.Create<IConnectionMultiplexer>(this, "multiplexer", database);
    }

    public class Recorder : DispatchProxy
    {
        private RecordingMultiplexer _owner = null!;
        private string _target = null!;
        private object? _next;

        internal static T Create<T>(RecordingMultiplexer owner, string target, object? next)
        {
            var proxy = Create<T, Recorder>();
            var recorder = (Recorder)(object)proxy!;
            recorder._owner = owner;
            recorder._target = target;
            recorder._next = next;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            lock (_owner.Calls)
                _owner.Calls.Add($"{_target}.{targetMethod.Name}");

            var type = targetMethod.ReturnType;
            if (targetMethod.Name is nameof(IConnectionMultiplexer.GetDatabase) or nameof(IDatabase.CreateTransaction))
                return _next;
            if (targetMethod.Name == nameof(ITransaction.ExecuteAsync))
                return Task.FromResult(true);
            if (type == typeof(Task))
                return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var result = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                    .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null]);
            }
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
