using Ananke.MQTT;
using Ananke.Redis;
using Ananke.StateMachine.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddStateMachine();
services.AddRedis("localhost");
services.AddMqtt<Device, Move>("localhost", 1883, "consumer");

await using var provider = services.BuildServiceProvider();
await Pause.RunAsync();
Console.WriteLine($"{services.Count} services");

internal static class Pause
{
    // An await without ConfigureAwait(false) in an internal method: this repository's own rule
    // (ANANKE_ASYNC_001) would flag it, and must not reach a product.
    internal static async Task RunAsync()
    {
        await Task.Yield();
    }
}

internal sealed class Device;

internal enum Move { Start, Stop }
