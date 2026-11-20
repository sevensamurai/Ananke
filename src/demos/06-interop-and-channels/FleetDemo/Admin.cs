using System.Text.Json;
using Ananke.Redis;
using Ananke.StateMachine;
using Microsoft.Extensions.Options;

namespace FleetDemo;

/// <summary>
/// What run.sh does besides running the processes: fault and reset a device, print the fleet as Valkey has it,
/// and check the fleet through a broker outage. Each opens the device machine on Valkey, as a node does.
/// </summary>
public static class Admin
{
    public static async Task<int> FaultAsync(string device, string reason, CancellationToken ct)
    {
        if (!Known(device)) return 2;
        await using var valkey = Open(out var machine);
        var change = await machine.FaultAsync(new DeviceReading { Id = device }, reason, ct);
        Console.WriteLine(change.Success
            ? $"{device} Faulted: both nodes refuse its readings until ./run.sh reset {device}"
            : $"{device} was already Faulted: {change.Reason}");
        return 0;
    }

    public static async Task<int> ResetAsync(string device, CancellationToken ct)
    {
        if (!Known(device)) return 2;
        await using var valkey = Open(out var machine);
        var change = await machine.ResetAsync(new DeviceReading { Id = device }, "reset by run.sh", ct);
        var status = await machine.GetStatusAsync(new DeviceReading { Id = device }, ct);
        Console.WriteLine(change.Success
            ? $"{device} Operative again: it moves on from {status.State} at step {status.Step}"
            : $"{device} was not Faulted");
        return 0;
    }

    public static async Task<int> StatusAsync(CancellationToken ct)
    {
        await using var valkey = Open(out var machine);
        var now = TimeProvider.System.GetUtcNow();
        foreach (var node in Fleet.Nodes)
        {
            var report = await valkey.GetValueAsync<NodeReport>(Fleet.NodeKey(node), ct);
            Console.WriteLine(report is null
                ? $"{node,-10} not running"
                : $"{node,-10} {(report.Connected ? "connected" : "not connected to the broker"),-28} " +
                  $"moved {report.Moved}, reconnects {report.Reconnects} (reported {Math.Max(0, (now - report.At).TotalSeconds):0} s ago)");
        }

        foreach (var device in Fleet.DeviceIds)
        {
            var status = await machine.GetStatusAsync(new DeviceReading { Id = device }, ct);
            var fault = status.OperationalStatus == OperationalStatus.Faulted ? $"  Faulted: {status.OperationalStatusReason}" : "";
            Console.WriteLine($"{device,-10} {status.State,-8} step {status.Step}{fault}");
        }

        return 0;
    }

    /// <summary>Writes the nodes' reports and the devices' state to <paramref name="file"/>.</summary>
    public static async Task<int> SnapshotAsync(string file, CancellationToken ct)
    {
        await using var valkey = Open(out var machine);
        var snapshot = new Snapshot([], []);
        foreach (var node in Fleet.Nodes)
        {
            if (await valkey.GetValueAsync<NodeReport>(Fleet.NodeKey(node), ct) is { } report)
                snapshot.Nodes[node] = report;
        }

        foreach (var device in Fleet.DeviceIds)
            snapshot.Devices[device] = await machine.GetStatusAsync(new DeviceReading { Id = device }, ct);

        if (snapshot.Nodes.Count == 0)
        {
            Console.Error.WriteLine("No node is running: start the demo with ./run.sh first.");
            return 1;
        }

        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(snapshot), ct);
        return 0;
    }

    /// <summary>
    /// After the broker is back: waits until every node that was running is connected and moving devices again,
    /// and every device that is not Faulted has moved on from its step at <paramref name="file"/>'s snapshot. A
    /// device whose step is lower than at the snapshot has lost its state. Fails after <paramref name="limit"/>.
    /// </summary>
    public static async Task<int> AwaitFlowAsync(string file, TimeSpan limit, CancellationToken ct)
    {
        var outage = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(file, ct))
                     ?? throw new InvalidOperationException($"{file} is empty");
        await using var valkey = Open(out var machine);

        var clock = TimeProvider.System;
        var start = clock.GetTimestamp();
        var operative = outage.Devices.Where(d => d.Value.OperationalStatus == OperationalStatus.Operative)
            .Select(d => d.Key).ToHashSet();
        var connected = new HashSet<string>();
        var moving = new HashSet<string>();
        var movedOn = new HashSet<string>();
        var latest = new Dictionary<string, ContextStatus<DeviceState>>();

        while (true)
        {
            var elapsed = clock.GetElapsedTime(start);
            foreach (var (node, before) in outage.Nodes)
            {
                var report = await valkey.GetValueAsync<NodeReport>(Fleet.NodeKey(node), ct);
                if (report is not { Connected: true }) continue;
                if (connected.Add(node))
                    Console.WriteLine($"{node} connected again {elapsed.TotalSeconds:0} s after the broker returned " +
                                      $"(reconnects {report.Reconnects})");
                if (report.Moved > before.Moved && moving.Add(node))
                    Console.WriteLine($"{node} moving devices again {elapsed.TotalSeconds:0} s after the broker returned");
            }

            foreach (var (device, before) in outage.Devices)
            {
                var now = await machine.GetStatusAsync(new DeviceReading { Id = device }, ct);
                if (now.Step < before.Step)
                {
                    Console.WriteLine($"{device} lost its state: step {before.Step} at the outage, {now.Step} now");
                    return 1;
                }

                latest[device] = now;
                // Faulted since the snapshot (./run.sh fault during the outage): it can't move on, so it isn't waited for.
                if (now.OperationalStatus == OperationalStatus.Faulted && !movedOn.Contains(device) && operative.Remove(device))
                    Console.WriteLine($"{device} Faulted since the outage: not waited for");
                if (operative.Contains(device) && now.Step > before.Step)
                    movedOn.Add(device);
            }

            if (moving.Count == outage.Nodes.Count && movedOn.Count == operative.Count)
            {
                var examples = movedOn.Order(Fleet.DeviceOrder).Take(3)
                    .Select(d => $"{d} {outage.Devices[d].Step}->{latest[d].Step}");
                Console.WriteLine($"Messages flow again: all {operative.Count} operative devices moved on from their step " +
                                  $"at the outage ({string.Join(", ", examples)}, ...); no state lost.");
                var faultedAtOutage = outage.Devices
                    .Where(d => d.Value.OperationalStatus == OperationalStatus.Faulted).Select(d => d.Key);
                foreach (var device in faultedAtOutage.Order(Fleet.DeviceOrder))
                {
                    var now = latest[device];
                    Console.WriteLine(now.OperationalStatus == OperationalStatus.Faulted && now.Step == outage.Devices[device].Step
                        ? $"{device} still Faulted at step {now.Step}, not moved"
                        : $"{device} was Faulted at the outage; now {now.OperationalStatus} at step {now.Step}");
                }

                return 0;
            }

            if (elapsed > limit)
            {
                string[] missing = [.. outage.Nodes.Keys.Except(moving), .. operative.Except(movedOn)];
                Console.WriteLine($"Not flowing again {limit.TotalSeconds:0} s after the broker returned: " +
                                  string.Join(' ', missing.Order(Fleet.DeviceOrder)));
                return 1;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private static RedisDistributedLock Open(out DeviceMachine machine)
    {
        var valkey = new RedisDistributedLock(Options.Create(Fleet.Valkey()), new ConsoleLog<RedisDistributedLock>("run.sh"));
        machine = new DeviceMachine(valkey, valkey);
        return valkey;
    }

    private static bool Known(string device)
    {
        if (Fleet.DeviceIds.Contains(device)) return true;
        Console.Error.WriteLine($"No device '{device}': the devices are {Fleet.DeviceIds[0]} to {Fleet.DeviceIds[^1]}.");
        return false;
    }

    private sealed record Snapshot(Dictionary<string, NodeReport> Nodes, Dictionary<string, ContextStatus<DeviceState>> Devices);
}
