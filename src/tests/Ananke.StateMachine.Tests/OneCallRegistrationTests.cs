using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.MQTT;
using Ananke.Redis;
using Ananke.StateMachine.Channels;
using Ananke.StateMachine.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class OneCallRegistrationTests
{
    [Test]
    public async Task AddStateMachine_UseRedisAndUseMqtt_ResolvesRedisLockAndMqttChannelsForBothEnums()
    {
        var services = new ServiceCollection();
        services.AddStateMachine<LightMachine, TestContext, Light, LightAction, LightNotify>(b => b
            .UseRedis(c => c.Host = "localhost")
            .UseMqtt(c => c.Namespace = "lights"));

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IActionStateMachine<TestContext, Light, LightAction, LightNotify>>()
            .ShouldBeOfType<LightMachine>();
        provider.GetRequiredService<IDistributedLock>().ShouldBeOfType<RedisDistributedLock>();
        provider.GetRequiredService<IKeyValueDataAdapter>().ShouldBeOfType<RedisDistributedLock>();
        provider.GetRequiredService<IChannelReader<TestContext, LightAction>>().ShouldBeOfType<MqttChannelReader<TestContext, LightAction>>();
        provider.GetRequiredService<IChannelWriter<LightAction>>().ShouldBeOfType<MqttChannelWriter<LightAction>>();
        provider.GetRequiredService<IChannelReader<TestContext, LightNotify>>().ShouldBeOfType<MqttChannelReader<TestContext, LightNotify>>();
        provider.GetRequiredService<IChannelWriter<LightNotify>>().ShouldBeOfType<MqttChannelWriter<LightNotify>>();
    }

    [Test]
    public async Task AddStateMachine_NoInfrastructure_ResolvesInMemoryLockAndLinkedChannelsForBothEnums()
    {
        var services = new ServiceCollection();
        services.AddStateMachine<LightMachine, TestContext, Light, LightAction, LightNotify>(_ => { });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLock>().ShouldBeOfType<InMemoryDistributedLock>();
        provider.GetRequiredService<IKeyValueDataAdapter>().ShouldBeOfType<InMemoryDistributedLock>();
        provider.GetRequiredService<IChannelReader<TestContext, LightAction>>().ShouldBeOfType<InMemoryChannelReader<TestContext, LightAction>>();
        provider.GetRequiredService<IChannelWriter<LightAction>>().ShouldBeOfType<InMemoryChannelWriter<LightAction>>();
        provider.GetRequiredService<IChannelReader<TestContext, LightNotify>>().ShouldBeOfType<InMemoryChannelReader<TestContext, LightNotify>>();
        provider.GetRequiredService<IChannelWriter<LightNotify>>().ShouldBeOfType<InMemoryChannelWriter<LightNotify>>();
    }

    [Test]
    public async Task AddStateMachine_HostRegisteredOnlyAWriter_AddsNoInMemoryReaderForThatEnum()
    {
        var services = new ServiceCollection();
        var hostWriter = new InMemoryChannelWriter<LightAction>();
        services.AddSingleton<IChannelWriter<LightAction>>(hostWriter);
        services.AddStateMachine<LightMachine, TestContext, Light, LightAction, LightNotify>(_ => { });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IChannelWriter<LightAction>>().ShouldBeSameAs(hostWriter);
        provider.GetService<IChannelReader<TestContext, LightAction>>().ShouldBeNull();
        provider.GetRequiredService<IChannelReader<TestContext, LightNotify>>().ShouldBeOfType<InMemoryChannelReader<TestContext, LightNotify>>();
    }

    [Test]
    public async Task AddStateMachine_NoInfrastructure_TransitionWrittenIsReadByTheMachine()
    {
        var services = new ServiceCollection();
        services.AddStateMachine<LightMachine, TestContext, Light, LightAction, LightNotify>(_ => { });
        await using var provider = services.BuildServiceProvider();
        var machine = provider.GetRequiredService<IActionStateMachine<TestContext, Light, LightAction, LightNotify>>();
        var transitioned = new TaskCompletionSource<TransitionResult<Light>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine)
        {
            OnTransition = (_, _, result) => transitioned.TrySetResult(result)
        };
        var reader = provider.GetRequiredService<IChannelReader<TestContext, LightAction>>();
        var writer = provider.GetRequiredService<IChannelWriter<LightAction>>();
        (await reader.ConfigureAsync(new ChannelConfig(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(new ChannelConfig())).ShouldBeTrue();

        (await writer.SendAsync(new TestContext("1"), LightAction.TurnOn)).Success.ShouldBeTrue();

        (await transitioned.Task.WaitAsync(TimeSpan.FromSeconds(5))).CurrentState.ShouldBe(Light.On);
        (await machine.GetStatusAsync(new TestContext("1"))).State.ShouldBe(Light.On);
    }
}
