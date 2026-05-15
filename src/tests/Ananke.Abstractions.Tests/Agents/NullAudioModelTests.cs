using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Abstractions.Tests.Agents;

[TestFixture]
public sealed class NullAudioModelTests
{
    private readonly IAudioModel _model = NullAudioModel.Instance;

    [Test]
    public async Task TranscribeAsync_ReturnsEmptyString()
    {
        var audio = new AudioPart(new byte[] { 1, 2, 3 }, "audio/wav");

        var result = await _model.TranscribeAsync(audio);

        result.ShouldBe(string.Empty);
    }

    [Test]
    public async Task SynthesizeAsync_ReturnsAudioPartWithEmptyData()
    {
        var result = await _model.SynthesizeAsync("hello");

        result.Data.ShouldBeEmpty();
        result.MimeType.ShouldBe("audio/wav");
    }

    [Test]
    public async Task SynthesizeAsync_WithOptions_IgnoresOptions()
    {
        var options = new AudioOptions { Voice = "alloy", SpeedFactor = 1.5f, Format = "audio/ogg" };

        var result = await _model.SynthesizeAsync("hello", options);

        result.Data.ShouldBeEmpty();
    }

    [Test]
    public void Instance_IsSingleton()
    {
        NullAudioModel.Instance.ShouldBeSameAs(NullAudioModel.Instance);
    }
}
