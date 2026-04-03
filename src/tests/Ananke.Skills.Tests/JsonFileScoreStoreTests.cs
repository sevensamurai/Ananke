using Shouldly;

namespace Ananke.Skills.Tests;

[TestFixture]
public class JsonFileScoreStoreTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ananke-scores-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task GetScoreAsync_NoVotes_ReturnsZero()
    {
        var store = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));

        var score = await store.GetScoreAsync("nonexistent");

        score.UpVotes.ShouldBe(0);
        score.DownVotes.ShouldBe(0);
        score.Net.ShouldBe(0);
    }

    [Test]
    public async Task RecordVoteAsync_UpVote_IncrementsUpVotes()
    {
        var store = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));

        await store.RecordVoteAsync("skill-a", VoteDirection.Up);
        await store.RecordVoteAsync("skill-a", VoteDirection.Up);

        var score = await store.GetScoreAsync("skill-a");
        score.UpVotes.ShouldBe(2);
        score.DownVotes.ShouldBe(0);
        score.Net.ShouldBe(2);
    }

    [Test]
    public async Task RecordVoteAsync_DownVote_IncrementsDownVotes()
    {
        var store = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));

        await store.RecordVoteAsync("skill-b", VoteDirection.Down);

        var score = await store.GetScoreAsync("skill-b");
        score.UpVotes.ShouldBe(0);
        score.DownVotes.ShouldBe(1);
        score.Net.ShouldBe(-1);
    }

    [Test]
    public async Task RecordVoteAsync_MixedVotes_CalculatesNet()
    {
        var store = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));

        await store.RecordVoteAsync("skill-c", VoteDirection.Up);
        await store.RecordVoteAsync("skill-c", VoteDirection.Up);
        await store.RecordVoteAsync("skill-c", VoteDirection.Up);
        await store.RecordVoteAsync("skill-c", VoteDirection.Down);

        var score = await store.GetScoreAsync("skill-c");
        score.UpVotes.ShouldBe(3);
        score.DownVotes.ShouldBe(1);
        score.Net.ShouldBe(2);
    }

    [Test]
    public async Task Scores_SurviveNewInstance()
    {
        var filePath = Path.Combine(_tempDir, "scores.json");

        var store1 = new JsonFileScoreStore(filePath);
        await store1.RecordVoteAsync("skill-d", VoteDirection.Up);

        // New instance — reads from same file
        var store2 = new JsonFileScoreStore(filePath);
        var score = await store2.GetScoreAsync("skill-d");

        score.UpVotes.ShouldBe(1);
    }

    [Test]
    public async Task GetAllScoresAsync_ReturnsAllVotedSkills()
    {
        var store = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));
        await store.RecordVoteAsync("skill-e", VoteDirection.Up);
        await store.RecordVoteAsync("skill-f", VoteDirection.Down);

        var all = await store.GetAllScoresAsync();

        all.Count.ShouldBe(2);
        all["skill-e"].Net.ShouldBe(1);
        all["skill-f"].Net.ShouldBe(-1);
    }
}
