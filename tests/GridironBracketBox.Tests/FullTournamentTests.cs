using GridironBracketBox.Core;

namespace GridironBracketBox.Tests;

/// <summary>Plays complete tournaments for every size in both modes and checks the invariants each step.</summary>
public class FullTournamentTests
{
    public static TheoryData<BracketMode, int> AllConfigs() => ByeRulesTests.AllConfigs();

    /// <summary>Repeatedly picks a winner for the first playable game until the bracket is finished.</summary>
    internal static void PlayOut(Bracket bracket, Func<Game, SeededTeam> choose)
    {
        for (int guard = 0; guard < 100; guard++)
        {
            var game = bracket.AllGames.FirstOrDefault(g => g.IsReady && !g.IsDecided);
            if (game is null) return;
            bracket.SetWinner(game, choose(game).Abbr);
            AssertReSeeded(bracket);
        }
        Assert.Fail("Bracket never finished.");
    }

    private static SeededTeam Favorite(Game g) => g.TeamB!.Seed < g.TeamA!.Seed ? g.TeamB : g.TeamA;
    private static SeededTeam Underdog(Game g) => g.TeamB!.Seed > g.TeamA!.Seed ? g.TeamB : g.TeamA!;

    /// <summary>In every known round: game 0 = best seed vs worst, game 1 = next best vs next worst, ...</summary>
    private static void AssertReSeeded(Bracket bracket)
    {
        foreach (var round in bracket.Rounds.Where(r => r.Section != BracketSection.Final && r.Games.All(g => g.IsReady)))
        {
            var highs = round.Games.Select(g => g.TeamA!.Seed).ToList();
            var lows = round.Games.Select(g => g.TeamB!.Seed).ToList();
            Assert.Equal(highs.Order(), highs);                // highs ascending: 1, 2, 3 ...
            Assert.Equal(lows.OrderDescending(), lows);         // lows descending: 8, 7, 6 ...
            Assert.True(highs.Max() < lows.Min(), $"{round.Section} R{round.Number} isn't paired high-vs-low.");
        }
    }

    [Theory]
    [MemberData(nameof(AllConfigs))]
    public void Fresh_bracket_has_right_shape(BracketMode mode, int size)
    {
        var bracket = new Bracket(BracketSetup.QuickFill(mode, size));
        int byes = ByeRules.ByesPerGroup(mode, size);
        int groupSize = ByeRules.GroupSize(mode, size);

        // Total game slots = size - 1 (each game eliminates exactly one team).
        Assert.Equal(size - 1, bracket.AllGames.Count());

        foreach (var section in bracket.Rounds.Select(r => r.Section).Distinct().Where(s => s != BracketSection.Final))
        {
            var round1 = bracket.Rounds.First(r => r.Section == section && r.Number == 1);
            var r1Seeds = round1.Games.SelectMany(g => new[] { g.TeamA!.Seed, g.TeamB!.Seed }).Order();
            // Round 1 is exactly the non-bye seeds; seeds 1..byes sit out.
            Assert.Equal(Enumerable.Range(byes + 1, groupSize - byes), r1Seeds);
            // Later rounds wait for results.
            Assert.All(bracket.Rounds.Where(r => r.Section == section && r.Number > 1).SelectMany(r => r.Games),
                g => Assert.False(g.IsReady));
        }

        if (mode == BracketMode.Conference)
            Assert.Single(bracket.Rounds, r => r.Section == BracketSection.Final);
        else
            Assert.DoesNotContain(bracket.Rounds, r => r.Section == BracketSection.Final);

        Assert.Null(bracket.Champion);
    }

    [Theory]
    [MemberData(nameof(AllConfigs))]
    public void Favorites_always_win(BracketMode mode, int size)
    {
        var bracket = new Bracket(BracketSetup.QuickFill(mode, size));
        PlayOut(bracket, Favorite);

        Assert.True(bracket.IsComplete);
        Assert.Equal(size - 1, bracket.Picks.Count);
        Assert.Equal(1, bracket.Champion!.Seed);
        if (mode == BracketMode.Conference)
        {
            var final = bracket.Rounds.Single(r => r.Section == BracketSection.Final).Games.Single();
            Assert.Equal(Conference.AFC, final.TeamA!.Team.Conference);
            Assert.Equal(Conference.NFC, final.TeamB!.Team.Conference);
            Assert.Equal((1, 1), (final.TeamA.Seed, final.TeamB.Seed));
        }
    }

    [Theory]
    [MemberData(nameof(AllConfigs))]
    public void Underdogs_always_win(BracketMode mode, int size)
    {
        var bracket = new Bracket(BracketSetup.QuickFill(mode, size));
        PlayOut(bracket, Underdog);

        // With re-seeding, the lowest seed beats everyone and survives every round.
        Assert.True(bracket.IsComplete);
        Assert.Equal(size - 1, bracket.Picks.Count);
        Assert.Equal(ByeRules.GroupSize(mode, size), bracket.Champion!.Seed);
    }

    [Theory]
    [MemberData(nameof(AllConfigs))]
    public void Random_tournaments_stay_consistent(BracketMode mode, int size)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var rng = new Random(seed);
            var bracket = new Bracket(BracketSetup.QuickFill(mode, size));
            PlayOut(bracket, g => rng.Next(2) == 0 ? g.TeamA! : g.TeamB!);

            Assert.True(bracket.IsComplete);
            Assert.Equal(size - 1, bracket.Picks.Count);
            // Each team loses at most once, and everyone but the champion loses exactly once.
            var losers = bracket.AllGames.Select(g => g.Loser!.Abbr).ToList();
            Assert.Equal(losers.Count, losers.Distinct().Count());
            Assert.DoesNotContain(bracket.Champion!.Abbr, losers);
            // Bye teams never appear in round 1.
            int byes = ByeRules.ByesPerGroup(mode, size);
            Assert.All(bracket.Rounds.Where(r => r.Number == 1).SelectMany(r => r.Games),
                g => Assert.True(g.TeamA!.Seed > byes && g.TeamB!.Seed > byes));
        }
    }
}
