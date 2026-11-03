using Ananke.Learning.Offline;
using Shouldly;

namespace Ananke.Learning.Tests;

/// <summary><see cref="OfflineLearner"/>'s centroid and weighted average over embeddings.</summary>
[TestFixture]
public class OfflineLearnerVectorTests
{
    [Test]
    public void ComputeCentroid_EmbeddingsOfDifferentLengths_Throws() =>
        Should.Throw<ArgumentException>(() => OfflineLearner.ComputeCentroid(
            [new float[] { 1, 2, 3 }, new float[] { 1, 2 }]));

    [Test]
    public void WeightedAverage_EmbeddingsOfDifferentLengths_Throws() =>
        Should.Throw<ArgumentException>(() => OfflineLearner.WeightedAverage(
            [(new float[] { 1, 2 }, 1f), (new float[] { 1, 2, 3 }, 1f)]));

    [Test]
    public void WeightedAverage_WeightsThatSumToZero_IsThePlainMean()
    {
        // Every weight zero leaves nothing to weight by; the mean is what is left to say.
        var average = OfflineLearner.WeightedAverage(
            [(new float[] { 1, 1 }, 0f), (new float[] { 3, 3 }, 0f)]);

        average.ToArray().ShouldBe([2f, 2f]);
    }
}
