using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.MQTT;
using Ananke.StateMachine;
using Ananke.StateMachine.Channels;
using Ananke.StateMachine.Middleware;

namespace StateMachineDemo;

/// <summary>
/// Section 4: MQTT-driven transitions over a real MQTT broker.
/// Demonstrates the same <see cref="CarEngineStateMachine"/> and <see cref="CarContext"/>
/// used in the in-memory sections — no duplicate types needed.
/// </summary>
static class MqttSection
{
    public static async Task RunAsync(StateMachineOptions options)
    {
        DemoConsole.Section("4. MQTT-driven transitions (requires Docker broker)");

        var mqttConfig = new ChannelConfig { Host = "localhost", Port = 1883, Namespace = "iot-demo" };

        var mqttLock = new InMemoryDistributedLock();
        var mqttEngine = new CarEngineStateMachine(mqttLock, mqttLock, options);
        mqttEngine.UseMiddleware(new LoggingMiddleware<CarContext, EngineState, EngineTransition>(
            msg => DemoConsole.Dim($"    ~ {msg}")));

        var mqttWorker = new StateMachineChannelWorker<CarContext, EngineState, EngineTransition, EngineNotification>(mqttEngine)
        {
            OnTransition = (ctx, transition, result) =>
            {
                if (result.Success)
                    DemoConsole.Say($"  ✓  {result.PreviousState,-10} --[{transition}]-->  {result.CurrentState,-10}  (via MQTT)");
                else
                    DemoConsole.Say($"  ✗  {result.PreviousState,-10} --[{transition}]-->  blocked     ({result.ErrorMessage})  (via MQTT)");
            }
        };

        await using var mqttReader = new MqttChannelReader<CarContext, EngineTransition>();
        var mqttWriter = new MqttChannelWriter<EngineTransition>();

        try
        {
            var readerOk = await mqttReader.ConfigureAsync(mqttConfig, mqttWorker);
            var writerOk = await mqttWriter.ConfigureAsync(mqttConfig);

            if (!readerOk || !writerOk)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                DemoConsole.Say("  ⚠ MQTT broker not available — skipping section 4.");
                DemoConsole.Say("    Start it with: docker compose -f demos/StateMachineDemo/docker-compose.yml up -d");
                Console.ResetColor();
            }
            else
            {
                DemoConsole.Say($"  Connected to MQTT broker at {mqttConfig.Host}:{mqttConfig.Port}");
                Console.WriteLine();

                var mqttCar = new CarContext { Id = "CAR-MQTT", Model = "Tesla", FuelLevel = 100.0 };
                DemoConsole.Say($"  Car: {mqttCar.Id} ({mqttCar.Model})");
                Console.WriteLine();

                await SendAsync(mqttWriter, mqttCar, EngineTransition.Start);
                await SendAsync(mqttWriter, mqttCar, EngineTransition.Drive);
                await SendAsync(mqttWriter, mqttCar, EngineTransition.Halt);
                await SendAsync(mqttWriter, mqttCar, EngineTransition.Park);

                DemoConsole.Say($"  Final state: {mqttEngine.CurrentState}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            DemoConsole.Say($"  ⚠ MQTT broker not available — skipping section 4. ({ex.Message})");
            DemoConsole.Say("    Start it with: docker compose -f demos/StateMachineDemo/docker-compose.yml up -d");
            Console.ResetColor();
        }
        finally
        {
            await mqttWriter.DisposeAsync();
        }
    }

    private static async Task SendAsync(
        MqttChannelWriter<EngineTransition> w,
        CarContext ctx,
        EngineTransition t)
    {
        var result = await w.SendAsync(ctx, t);
        if (!result.Success)
            Console.WriteLine($"  ✗  SendAsync({t}) failed: {result.ErrorMessage}");

        // Small delay so the broker can deliver to the reader
        await Task.Delay(50);
    }
}
