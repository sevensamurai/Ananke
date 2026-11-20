using Shouldly;

namespace Ananke.MQTT.Tests;

[TestFixture]
public class MqttReconnectDelayTests
{
    [Test]
    public void For_EachAttempt_DoublesUpToTheCapLessAtMostAQuarter()
    {
        var delay = new MqttReconnectDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        double[] expected = [1, 2, 4, 8, 16, 30, 30, 30];

        for (var attempt = 0; attempt < expected.Length; attempt++)
        {
            for (var sample = 0; sample < 50; sample++)
            {
                var wait = delay.For(attempt).TotalSeconds;
                wait.ShouldBeLessThanOrEqualTo(expected[attempt]);
                wait.ShouldBeGreaterThanOrEqualTo(expected[attempt] * 0.75);
            }
        }
    }

    [Test]
    public void For_ALargeAttemptNumber_StaysAtTheCap()
    {
        var delay = MqttReconnectDelay.Default;

        delay.For(int.MaxValue).ShouldBeLessThanOrEqualTo(delay.Max);
        delay.For(int.MaxValue).ShouldBeGreaterThanOrEqualTo(delay.Max * 0.75);
    }
}
