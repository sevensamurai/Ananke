using Ananke.Abstractions.Config;
using Shouldly;

namespace Ananke.MQTT.Tests;

/// <summary>The client ids <see cref="MqttConnection.OptionsFor"/> derives from <see cref="ChannelConfig.ClientId"/>.</summary>
[TestFixture]
public class MqttClientIdTests
{
    private static ChannelConfig Config(string @namespace = "test") =>
        new() { ClientId = "a-client-id-longer-than-fourteen", Namespace = @namespace };

    private static string IdFor(ChannelConfig config, params string[] client) =>
        MqttConnection.OptionsFor(config, client).ClientId;

    [Test]
    public void OptionsFor_SameConfigTwice_SameIdOfAtMost23Characters()
    {
        string[] reader = ["reader", typeof(PingAction).ToString(), typeof(PingMessage).ToString()];

        var first = IdFor(Config(), reader);
        var second = IdFor(Config(), reader);

        first.ShouldBe(second);
        first.Length.ShouldBeLessThanOrEqualTo(23);
        first.ShouldStartWith("a-client-id-lo-");
        // A SHA-256, not string.GetHashCode, which differs between processes: the same value in every run
        // (printf 'a-client-id-longer-than-fourteen\nreader\nAnanke.MQTT.Tests.PingAction\nAnanke.MQTT.Tests.PingMessage\ntest' | sha256sum).
        first.ShouldBe("a-client-id-lo-cc506362");
    }

    [Test]
    public void OptionsFor_ShortClientId_KeptWhole()
    {
        var config = new ChannelConfig { ClientId = "api", Namespace = "test" };

        IdFor(config, "writer", typeof(PingAction).ToString()).ShouldMatch("^api-[0-9a-f]{8}$");
    }

    [Test]
    public void OptionsFor_ClientIdsWithTheSameFirst14Characters_DifferentIds()
    {
        var one = new ChannelConfig { ClientId = "seren-api-6f7c9d8b5-x2k4p", Namespace = "test" };
        var two = new ChannelConfig { ClientId = "seren-api-6f7c9d8b5-q8m1z", Namespace = "test" };

        IdFor(one, "writer", typeof(PingAction).ToString()).ShouldNotBe(IdFor(two, "writer", typeof(PingAction).ToString()));
    }

    [Test]
    public void OptionsFor_ReadersOfOneActionTypeWithDifferentMessageTypes_DifferentIds()
    {
        var action = typeof(PingAction).ToString();

        IdFor(Config(), "reader", action, typeof(PingMessage).ToString())
            .ShouldNotBe(IdFor(Config(), "reader", action, typeof(OtherMessage).ToString()));
    }

    [Test]
    public void OptionsFor_ReadersInDifferentNamespaces_DifferentIds()
    {
        string[] reader = ["reader", typeof(PingAction).ToString(), typeof(PingMessage).ToString()];

        IdFor(Config("one"), reader).ShouldNotBe(IdFor(Config("two"), reader));
    }

    [Test]
    public void OptionsFor_HandoffChannelsWithDifferentNames_DifferentIds()
    {
        string[] ids = [IdFor(Config(), "handoff"), IdFor(Config(), "handoff", "a"), IdFor(Config(), "handoff", "b")];

        ids.Distinct().Count().ShouldBe(3);
    }

    [Test]
    public void OptionsFor_ReaderAndWriterOfOneActionType_DifferentIds()
    {
        var action = typeof(PingAction).ToString();

        IdFor(Config(), "reader", action, typeof(PingMessage).ToString())
            .ShouldNotBe(IdFor(Config(), "writer", action));
    }
}

public sealed class OtherMessage
{
    public string Text { get; set; } = string.Empty;
}
