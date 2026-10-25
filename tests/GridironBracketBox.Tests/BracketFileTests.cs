using GridironBracketBox.Core;

namespace GridironBracketBox.Tests;

public class BracketFileTests
{
    /// <summary>A random setup with a random number of games picked (0 to all).</summary>
    private static Bracket PartlyPlayed(BracketMode mode, int size, int seed)
    {
        var rng = new Random(seed);
        var bracket = new Bracket(BracketSetup.Randomize(mode, size, rng));
        int picks = rng.Next(size); // size - 1 games total
        for (int i = 0; i < picks; i++)
        {
            var game = bracket.AllGames.FirstOrDefault(g => g.IsReady && !g.IsDecided);
            if (game is null) break;
            bracket.SetWinner(game, (rng.Next(2) == 0 ? game.TeamA! : game.TeamB!).Abbr);
        }
        return bracket;
    }

    [Theory]
    [MemberData(nameof(ByeRulesTests.AllConfigs), MemberType = typeof(ByeRulesTests))]
    public void Save_then_load_gives_the_same_bracket(BracketMode mode, int size)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var original = PartlyPlayed(mode, size, seed);
            var loaded = BracketFile.Deserialize(BracketFile.Serialize(original));

            Assert.Equal(original.Setup.Mode, loaded.Setup.Mode);
            Assert.Equal(original.Setup.Size, loaded.Setup.Size);
            Assert.Equal(original.Setup.Entries.OrderBy(e => e.Abbr), loaded.Setup.Entries.OrderBy(e => e.Abbr));
            Assert.Equal(original.Picks.OrderBy(p => p.Winner), loaded.Picks.OrderBy(p => p.Winner));
            Assert.Equal(original.Champion, loaded.Champion);
            // Every game slot matches: same teams, same winner.
            Assert.Equal(original.AllGames, loaded.AllGames);
        }
    }

    [Fact]
    public void File_is_readable_json()
    {
        var bracket = new Bracket(BracketSetup.QuickFill(BracketMode.Conference, 14));
        string json = BracketFile.Serialize(bracket, new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("\"format\": \"gridiron-bracket-box\"", json);
        Assert.Contains("\"mode\": \"Conference\"", json);   // enum as a word, not a number
        Assert.Contains("\"abbr\": \"BUF\"", json);
        Assert.Contains("\n", json);                         // indented, one value per line
    }

    [Fact]
    public void Not_json_gives_friendly_error()
    {
        var ex = Assert.Throws<BracketFileException>(() => BracketFile.Deserialize("this is not json {"));
        Assert.Contains("couldn't be read", ex.Message);
    }

    [Fact]
    public void Other_json_is_rejected()
    {
        var ex = Assert.Throws<BracketFileException>(() => BracketFile.Deserialize("""{ "name": "my grocery list" }"""));
        Assert.Contains("isn't a Gridiron Bracket Box file", ex.Message);
    }

    [Fact]
    public void Newer_version_is_rejected()
    {
        string json = BracketFile.Serialize(new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8)))
            .Replace("\"version\": 1", "\"version\": 99");
        var ex = Assert.Throws<BracketFileException>(() => BracketFile.Deserialize(json));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void Invalid_setup_is_rejected_with_reason()
    {
        string json = BracketFile.Serialize(new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8)))
            .Replace("\"abbr\": \"MIA\"", "\"abbr\": \"BUF\""); // BUF twice
        var ex = Assert.Throws<BracketFileException>(() => BracketFile.Deserialize(json));
        Assert.Contains("BUF is picked more than once", ex.Message);
    }

    [Fact]
    public void Picks_that_no_longer_fit_are_dropped()
    {
        var bracket = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        bracket.SetWinner(bracket.Rounds[0].Games[0], "BUF");
        string json = BracketFile.Serialize(bracket)
            .Replace("\"loser\": \"PIT\"", "\"loser\": \"SEA\""); // SEA isn't even in this bracket
        Assert.Empty(BracketFile.Deserialize(json).Picks);
    }

    [Fact]
    public void Missing_picks_means_no_picks()
    {
        string json = """
            { "format": "gridiron-bracket-box", "version": 1, "mode": "Open", "size": 8,
              "teams": [ {"abbr":"BUF","seed":1},{"abbr":"MIA","seed":2},{"abbr":"NE","seed":3},{"abbr":"NYJ","seed":4},
                         {"abbr":"BAL","seed":5},{"abbr":"CIN","seed":6},{"abbr":"CLE","seed":7},{"abbr":"PIT","seed":8} ] }
            """;
        var bracket = BracketFile.Deserialize(json);
        Assert.Empty(bracket.Picks);
        Assert.Equal(7, bracket.AllGames.Count());
    }
}
