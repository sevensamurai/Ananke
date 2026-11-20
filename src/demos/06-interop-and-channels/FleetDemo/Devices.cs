using Ananke.Abstractions.Channels;
using Ananke.MQTT;

namespace FleetDemo;

/// <summary>
/// The 20 simulated devices: each sends a reading every 2 s, on Move while it drives and on Stop while it is
/// parked, and the process counts the updates the nodes send back. Prints both every 5 s.
/// </summary>
public static class Devices
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    private const int TicksPerReading = 20;

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        const string name = "devices";
        await using var readings = new MqttChannelWriter<DeviceTransition>(new ConsoleLog<MqttChannelWriter<DeviceTransition>>(name));
        var back = new UpdateCounter();
        await using var updates = new MqttChannelReader<DeviceUpdate, DeviceNotification>(
            logger: new ConsoleLog<MqttChannelReader<DeviceUpdate, DeviceNotification>>(name));

        await readings.ConfigureAsync(Fleet.Mosquitto(name), ct);
        await updates.ConfigureAsync(Fleet.Mosquitto(name), back, ct);
        Console.WriteLine($"{name}  up: {Fleet.DeviceCount} devices, each sending a reading every 2 s");

        var devices = Fleet.DeviceIds.Select((id, i) => new SimulatedDevice(id, new Random(i))).ToArray();
        int sent = 0, notSent = 0;
        using var timer = new PeriodicTimer(Tick);
        try
        {
            for (var tick = 0L; await timer.WaitForNextTickAsync(ct); tick++)
            {
                // Each device on its own tick of a 2 s round, one tick earlier each round: a device's readings are
                // 1.9 s apart (3.9 s once a round), never close enough for two nodes to apply them out of order.
                // The broker hands a shared subscription's messages to the nodes in turn, so moving a device by
                // one tick sends its readings to the nodes alternately, not always to the same one.
                var slot = tick % TicksPerReading;
                var round = tick / TicksPerReading;
                foreach (var device in devices.Where((_, i) => ((i - round) % TicksPerReading + TicksPerReading) % TicksPerReading == slot))
                {
                    var (reading, transition) = device.Next();
                    // Fails at once while the writer is not connected to the broker.
                    if ((await readings.SendAsync(reading, transition, ct)).Success)
                        sent++;
                    else
                        notSent++;
                }

                if (tick % 50 == 49)
                {
                    var line = $"{name}  sent {sent} readings, {back.Take()} updates back";
                    if (notSent > 0) line += $", {notSent} not sent (not connected to the broker)";
                    Console.WriteLine(line);
                    sent = notSent = 0;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        Console.WriteLine($"{name}  stopped");
        return 0;
    }

    /// <summary>Drives for 3 to 8 readings, then parks for 2 to 5.</summary>
    private sealed class SimulatedDevice(string id, Random random)
    {
        private bool _moving = random.Next(2) == 0;
        private int _left = random.Next(1, 6);

        public (DeviceReading Reading, DeviceTransition Transition) Next()
        {
            if (_left-- <= 0)
            {
                _moving = !_moving;
                _left = _moving ? random.Next(3, 9) : random.Next(2, 6);
            }

            return _moving
                ? (new DeviceReading { Id = id, SpeedKmh = random.Next(20, 90) }, DeviceTransition.Move)
                : (new DeviceReading { Id = id, SpeedKmh = 0 }, DeviceTransition.Stop);
        }
    }

    private sealed class UpdateCounter : IBackgroundWorker<DeviceUpdate, DeviceNotification>
    {
        private int _count;

        public Task HandleAsync(DeviceUpdate? item, DeviceNotification action, CancellationToken token)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }

        public int Take() => Interlocked.Exchange(ref _count, 0);
    }
}
