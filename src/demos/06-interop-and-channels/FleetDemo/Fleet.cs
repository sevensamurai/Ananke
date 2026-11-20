using Ananke.Abstractions;
using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;

namespace FleetDemo;

public enum DeviceState { Parked, Moving }

/// <summary>The topic a reading arrives on: <c>fleet/devicetransition/move</c> or <c>…/stop</c>.</summary>
public enum DeviceTransition { Move, Stop }

/// <summary>The topic a node sends a device's change of state to: <c>fleet/devicenotification/statechanged</c>.</summary>
public enum DeviceNotification { StateChanged }

/// <summary>A device's reading, sent on Move while it moves and on Stop while it is parked.</summary>
public sealed class DeviceReading : IBaseContext
{
    public string Id { get; set; } = "";
    public double SpeedKmh { get; set; }
}

/// <summary>What a node sends out when a device's state changes.</summary>
public sealed record DeviceUpdate
{
    public string DeviceId { get; init; } = "";
    public DeviceState From { get; init; }
    public DeviceState To { get; init; }
    public string Node { get; init; } = "";
}

/// <summary>What a node writes to Valkey every second, for <c>status</c> and the broker-outage check.</summary>
/// <param name="Name">The node's name.</param>
/// <param name="Connected">Its reader and writer are both connected to the broker.</param>
/// <param name="Moved">Transitions it has run since it started.</param>
/// <param name="Reconnects">Times it has connected again after losing the broker.</param>
/// <param name="At">When it wrote the report.</param>
public sealed record NodeReport(string Name, bool Connected, long Moved, int Reconnects, DateTimeOffset At);

/// <summary>
/// One machine for every device: each device is a context, its state, step and fault kept in Valkey under
/// <c>fleetdemo:statemachine:device:{device id}</c>, each transition run under that device's lock.
/// </summary>
public sealed class DeviceMachine(IDistributedLock locker, IKeyValueDataAdapter store)
    : AbstractStateMachine<DeviceReading, DeviceState, DeviceTransition, DeviceNotification>(
        DeviceState.Parked, locker, store, new StateMachineOptions { MachineName = "device" })
{
    // A reading on Move while Moving (or Stop while Parked) is an implicit self-transition: the step still counts.
    protected override Action<ITransitionBuilder<DeviceState, DeviceTransition>> Transitions => b => b
        .From(DeviceState.Parked).On(DeviceTransition.Move).To(DeviceState.Moving)
        .From(DeviceState.Moving).On(DeviceTransition.Stop).To(DeviceState.Parked);

    public override Task<TransitionResult<DeviceState>> TransitionAsync(
        DeviceReading context, DeviceTransition transition, CancellationToken ct = default)
        => InternalTransitionAsync(context, transition, ct);

    public override Task NotifyAsync(DeviceReading context, DeviceNotification notification, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>The demo's names, and the ports run.sh passes from devenv.</summary>
public static class Fleet
{
    public const int DeviceCount = 20;

    public static readonly string[] Nodes = ["node-a", "node-b"];

    public static IReadOnlyList<string> DeviceIds { get; } =
        [.. Enumerable.Range(1, DeviceCount).Select(i => $"device-{i}")];

    /// <summary>Orders device ids by their number: device-2 before device-10.</summary>
    public static IComparer<string> DeviceOrder { get; } = Comparer<string>.Create((a, b) =>
        Number(a).CompareTo(Number(b)) is var byNumber and not 0 ? byNumber : string.CompareOrdinal(a, b));

    public static CacheConfig Valkey() => new()
    {
        Host = "127.0.0.1",
        Port = Port("FLEET_VALKEY_PORT"),
        KeyPrefix = "fleetdemo:",
    };

    /// <param name="clientId">The start of each client's id on the broker, so the broker's log names the process.</param>
    /// <param name="group">A shared subscription: the broker hands each message to one subscriber of the group.</param>
    public static ChannelConfig Mosquitto(string clientId, string? group = null) => new()
    {
        Host = "127.0.0.1",
        Port = Port("FLEET_MOSQUITTO_PORT"),
        Namespace = "fleet",
        ClientId = clientId,
        GroupName = group,
    };

    public static string NodeKey(string node) => $"node:{node}";

    private static int Number(string deviceId) =>
        int.TryParse(deviceId.AsSpan(deviceId.LastIndexOf('-') + 1), out var n) ? n : int.MaxValue;

    private static int Port(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var port)
            ? port
            : throw new InvalidOperationException($"{name} is not set: start the demo with ./run.sh");
}
