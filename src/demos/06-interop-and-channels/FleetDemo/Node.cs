using Ananke.Abstractions.Channels;
using Ananke.MQTT;
using Ananke.Redis;
using Ananke.StateMachine;
using Ananke.StateMachine.Channels;
using Microsoft.Extensions.Options;

namespace FleetDemo;

/// <summary>
/// A node: the device machine, fed by the devices' readings from a shared subscription (the broker hands each
/// reading to one of the nodes), its state and locks in Valkey, and each change of state sent out over MQTT.
/// Prints the devices it moved every 5 s.
/// </summary>
public static class Node
{
    public static async Task<int> RunAsync(string name, CancellationToken ct)
    {
        await using var valkey = new RedisDistributedLock(
            Options.Create(Fleet.Valkey()), new ConsoleLog<RedisDistributedLock>(name));
        var machine = new DeviceMachine(valkey, valkey);
        var moves = new Moves();

        await using var updates = new MqttChannelWriter<DeviceNotification>(
            new ConsoleLog<MqttChannelWriter<DeviceNotification>>(name));
        await using var worker = new StateMachineChannelWorker<DeviceReading, DeviceState, DeviceTransition, DeviceNotification>(
            machine,
            new UpdateSender(name, updates),
            new ConsoleLog<StateMachineChannelWorker<DeviceReading, DeviceState, DeviceTransition, DeviceNotification>>(name))
        {
            OnTransition = moves.Record,
        };
        // Declared last, so disposed first: no reading arrives once the worker and the writer are going.
        await using var readings = new MqttChannelReader<DeviceReading, DeviceTransition>(
            logger: new ConsoleLog<MqttChannelReader<DeviceReading, DeviceTransition>>(name));

        await updates.ConfigureAsync(Fleet.Mosquitto(name), ct);
        await readings.ConfigureAsync(Fleet.Mosquitto(name, group: "nodes"), worker, ct);
        Console.WriteLine($"{name}  up: readings from fleet/devicetransition/+ shared with the other node, " +
                          "updates to fleet/devicenotification/statechanged, state and locks in Valkey");

        var wasConnected = readings.IsConnected && updates.IsConnected;
        var reconnects = 0;
        var seconds = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var connected = readings.IsConnected && updates.IsConnected;
                if (connected && !wasConnected) reconnects++;
                wasConnected = connected;

                await ReportAsync(valkey, new NodeReport(name, connected, moves.Total, reconnects, TimeProvider.System.GetUtcNow()), ct);
                if (++seconds % 5 == 0)
                    Console.WriteLine(moves.Take(name, connected));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        Console.WriteLine($"{name}  stopped");
        return 0;
    }

    // Expires, so a node that stopped no longer shows as running.
    private static async Task ReportAsync(RedisDistributedLock valkey, NodeReport report, CancellationToken ct)
    {
        try
        {
            await valkey.SetValueAsync(Fleet.NodeKey(report.Name), report, TimeSpan.FromSeconds(5), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"{report.Name}  warning: could not write its report to Valkey ({ex.Message})");
        }
    }

    /// <summary>Sends a device's change of state out; a self-transition sends nothing.</summary>
    private sealed class UpdateSender(string node, IChannelWriter<DeviceNotification> writer)
        : IBackgroundWorker<TransitionEvent<DeviceReading, DeviceState, DeviceTransition>>
    {
        public async Task HandleAsync(TransitionEvent<DeviceReading, DeviceState, DeviceTransition>? item, CancellationToken token)
        {
            if (item is not { Result.Success: true } || item.Result.PreviousState == item.Result.CurrentState)
                return;

            // Not sent while the broker is away; the state itself is in Valkey.
            await writer.SendAsync(new DeviceUpdate
            {
                DeviceId = item.Context.Id,
                From = item.Result.PreviousState,
                To = item.Result.CurrentState,
                Node = node,
            }, DeviceNotification.StateChanged, token);
        }
    }

    /// <summary>The devices a node moved and the ones refused since the last summary.</summary>
    private sealed class Moves
    {
        private readonly Lock _gate = new();
        private readonly SortedSet<string> _moved = new(Fleet.DeviceOrder);
        private readonly SortedDictionary<string, string> _refused = new(Fleet.DeviceOrder);
        private long _total;

        public long Total => Interlocked.Read(ref _total);

        public void Record(DeviceReading device, DeviceTransition transition, TransitionResult<DeviceState> result)
        {
            lock (_gate)
            {
                if (result.Success)
                {
                    _moved.Add(device.Id);
                    Interlocked.Increment(ref _total);
                }
                else
                {
                    _refused[device.Id] = result.ErrorMessage ?? "refused";
                }
            }
        }

        public string Take(string node, bool connected)
        {
            lock (_gate)
            {
                var line = _moved.Count == 0
                    ? $"{node}  moved none{(connected ? "" : " (not connected to the broker)")}"
                    : $"{node}  moved {_moved.Count}: {string.Join(' ', _moved)}";
                if (_refused.Count > 0)
                    line += $"  | refused: {string.Join(", ", _refused.Select(r => $"{r.Key} ({r.Value})"))}";
                _moved.Clear();
                _refused.Clear();
                return line;
            }
        }
    }
}
