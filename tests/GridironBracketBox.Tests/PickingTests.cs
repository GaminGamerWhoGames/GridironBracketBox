using GridironBracketBox.Core;

namespace GridironBracketBox.Tests;

public class PickingTests
{
    private static Game GameWith(Bracket b, BracketSection section, int round, int seed) =>
        b.Rounds.Single(r => r.Section == section && r.Number == round)
            .Games.Single(g => g.TeamA?.Seed == seed || g.TeamB?.Seed == seed);

    /// <summary>Picks the team with <paramref name="seed"/> to win its game in the given round.</summary>
    private static void Win(Bracket b, BracketSection section, int round, int seed)
    {
        var g = GameWith(b, section, round, seed);
        b.SetWinner(g, (g.TeamA!.Seed == seed ? g.TeamA : g.TeamB!).Abbr);
    }

    private static List<(int, int)> Matchups(Bracket b, BracketSection section, int round) =>
        b.Rounds.Single(r => r.Section == section && r.Number == round)
            .Games.Select(g => (g.TeamA!.Seed, g.TeamB!.Seed)).ToList();

    // ---- Re-seeding -------------------------------------------------------

    [Fact]
    public void Open8_round1_is_1v8_2v7_3v6_4v5()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        Assert.Equal([(1, 8), (2, 7), (3, 6), (4, 5)], Matchups(b, BracketSection.Open, 1));
    }

    [Fact]
    public void Open8_reseeds_after_upsets()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        foreach (var s in new[] { 8, 2, 3, 5 }) Win(b, BracketSection.Open, 1, s);
        Assert.Equal([(2, 8), (3, 5)], Matchups(b, BracketSection.Open, 2));
    }

    [Fact]
    public void Conference14_top_seed_plays_lowest_survivor()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Conference, 14));
        Assert.Equal([(2, 7), (3, 6), (4, 5)], Matchups(b, BracketSection.AFC, 1));
        foreach (var s in new[] { 7, 6, 5 }) Win(b, BracketSection.AFC, 1, s);
        Assert.Equal([(1, 7), (5, 6)], Matchups(b, BracketSection.AFC, 2));
    }

    [Fact]
    public void Conference12_two_byes_join_in_round2()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Conference, 12));
        Assert.Equal([(3, 6), (4, 5)], Matchups(b, BracketSection.NFC, 1));
        Win(b, BracketSection.NFC, 1, 6);
        Win(b, BracketSection.NFC, 1, 4);
        Assert.Equal([(1, 6), (2, 4)], Matchups(b, BracketSection.NFC, 2));
    }

    [Fact]
    public void Open12_mixed_results_reseed()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 12));
        Assert.Equal([(5, 12), (6, 11), (7, 10), (8, 9)], Matchups(b, BracketSection.Open, 1));
        foreach (var s in new[] { 5, 11, 7, 9 }) Win(b, BracketSection.Open, 1, s);
        Assert.Equal([(1, 11), (2, 9), (3, 7), (4, 5)], Matchups(b, BracketSection.Open, 2));
    }

    [Fact]
    public void Open14_two_byes()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 14));
        Assert.Equal([(3, 14), (4, 13), (5, 12), (6, 11), (7, 10), (8, 9)], Matchups(b, BracketSection.Open, 1));
        foreach (var s in new[] { 14, 4, 12, 6, 10, 8 }) Win(b, BracketSection.Open, 1, s);
        Assert.Equal([(1, 14), (2, 12), (4, 10), (6, 8)], Matchups(b, BracketSection.Open, 2));
    }

    [Fact]
    public void Next_round_waits_until_current_round_is_complete()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        foreach (var s in new[] { 1, 2, 3 }) Win(b, BracketSection.Open, 1, s);
        Assert.All(b.Rounds.Single(r => r.Number == 2).Games, g => Assert.False(g.IsReady));
    }

    // ---- Changing, clearing, undo, reset -----------------------------------

    private static Bracket Open8WithRound2Picked()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        foreach (var s in new[] { 1, 2, 3, 4 }) Win(b, BracketSection.Open, 1, s);
        Win(b, BracketSection.Open, 2, 1); // 1 beats 4
        Win(b, BracketSection.Open, 2, 2); // 2 beats 3
        return b;
    }

    [Fact]
    public void Changing_a_pick_clears_only_dependent_picks()
    {
        var b = Open8WithRound2Picked();
        Win(b, BracketSection.Open, 1, 5); // flip 4v5: now R2 is 1v5 and 2v3

        Assert.Equal([(1, 5), (2, 3)], Matchups(b, BracketSection.Open, 2));
        Assert.False(GameWith(b, BracketSection.Open, 2, 1).IsDecided); // 1v4 pick no longer applies
        Assert.Equal(2, GameWith(b, BracketSection.Open, 2, 2).Winner!.Seed); // 2v3 unchanged, kept
        Assert.Equal(5, b.Picks.Count);
    }

    [Fact]
    public void Clearing_a_pick_clears_later_rounds()
    {
        var b = Open8WithRound2Picked();
        b.ClearWinner(GameWith(b, BracketSection.Open, 1, 4));

        Assert.Equal(3, b.Picks.Count);
        Assert.All(b.Rounds.Where(r => r.Number > 1).SelectMany(r => r.Games), g => Assert.False(g.IsReady));
    }

    [Fact]
    public void Changing_AFC_pick_leaves_NFC_alone()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Conference, 8));
        foreach (var sec in new[] { BracketSection.AFC, BracketSection.NFC })
        {
            Win(b, sec, 1, 1); Win(b, sec, 1, 2); Win(b, sec, 2, 1);
        }
        var final = b.Rounds.Single(r => r.Section == BracketSection.Final).Games[0];
        b.SetWinner(final, final.TeamB!.Abbr);
        Assert.Equal(Conference.NFC, b.Champion!.Team.Conference);

        Win(b, BracketSection.AFC, 1, 4); // AFC upset changes the AFC title game and the final

        Assert.Null(b.Champion);
        Assert.False(b.Rounds.Single(r => r.Section == BracketSection.Final).Games[0].IsReady);
        Assert.Equal(3, b.Picks.Count(p => NflTeams.Find(p.Winner)!.Conference == Conference.NFC));
    }

    [Fact]
    public void Undo_restores_previous_picks()
    {
        var b = Open8WithRound2Picked();
        var before = b.Picks.ToList();
        Win(b, BracketSection.Open, 1, 5);
        Assert.True(b.Undo());
        Assert.Equal(before, b.Picks);
        Assert.Equal(1, GameWith(b, BracketSection.Open, 2, 1).Winner!.Seed);
    }

    [Fact]
    public void Reset_clears_everything_and_is_undoable()
    {
        var b = Open8WithRound2Picked();
        b.Reset();
        Assert.Empty(b.Picks);
        Assert.True(b.Undo());
        Assert.Equal(6, b.Picks.Count);
    }

    [Fact]
    public void Undo_with_no_history_returns_false()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        Assert.False(b.CanUndo);
        Assert.False(b.Undo());
    }

    [Fact]
    public void Repicking_same_winner_is_a_no_op()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        Win(b, BracketSection.Open, 1, 1);
        Assert.True(b.Undo());
        Win(b, BracketSection.Open, 1, 1);
        Win(b, BracketSection.Open, 1, 1);
        Assert.True(b.Undo());
        Assert.False(b.CanUndo); // second identical pick didn't add history
    }

    [Fact]
    public void Changed_event_fires_on_pick()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        int fired = 0;
        b.Changed += (_, _) => fired++;
        Win(b, BracketSection.Open, 1, 1);
        Assert.Equal(1, fired);
    }

    // ---- Bad input --------------------------------------------------------

    [Fact]
    public void Cannot_pick_a_game_without_both_teams()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        var placeholder = b.Rounds.Single(r => r.Number == 2).Games[0];
        Assert.Throws<InvalidOperationException>(() => b.SetWinner(placeholder, "BUF"));
    }

    [Fact]
    public void Cannot_pick_a_team_not_in_the_game()
    {
        var b = new Bracket(BracketSetup.QuickFill(BracketMode.Open, 8));
        var g = b.Rounds[0].Games[0];
        Assert.Throws<ArgumentException>(() => b.SetWinner(g, "SEA"));
    }

    [Fact]
    public void Loaded_picks_that_match_no_game_are_dropped()
    {
        var setup = BracketSetup.QuickFill(BracketMode.Open, 8);
        var b = new Bracket(setup, [new Pick(1, "BUF", "PIT"), new Pick(1, "BUF", "SEA"), new Pick(3, "MIA", "NE")]);
        Assert.Equal([new Pick(1, "BUF", "PIT")], b.Picks);
    }

    [Theory]
    [MemberData(nameof(ByeRulesTests.AllConfigs), MemberType = typeof(ByeRulesTests))]
    public void ByeTeams_are_the_top_seeds_of_each_section(BracketMode mode, int size)
    {
        var b = new Bracket(BracketSetup.QuickFill(mode, size));
        int byes = ByeRules.ByesPerGroup(mode, size);
        var sections = mode == BracketMode.Open
            ? new[] { BracketSection.Open }
            : new[] { BracketSection.AFC, BracketSection.NFC };
        foreach (var section in sections)
            Assert.Equal(Enumerable.Range(1, byes), b.ByeTeams(section).Select(t => t.Seed));
        Assert.Empty(b.ByeTeams(BracketSection.Final));
    }

    // ---- Round names --------------------------------------------------------

    [Theory]
    [InlineData(BracketMode.Conference, 8, "Divisional Round|Conference Championship|Super Bowl")]
    [InlineData(BracketMode.Conference, 12, "Wild Card Round|Divisional Round|Conference Championship|Super Bowl")]
    [InlineData(BracketMode.Conference, 14, "Wild Card Round|Divisional Round|Conference Championship|Super Bowl")]
    [InlineData(BracketMode.Conference, 16, "Wild Card Round|Divisional Round|Conference Championship|Super Bowl")]
    [InlineData(BracketMode.Conference, 32, "First Round|Wild Card Round|Divisional Round|Conference Championship|Super Bowl")]
    [InlineData(BracketMode.Open, 8, "Quarterfinals|Semifinals|Super Bowl")]
    [InlineData(BracketMode.Open, 12, "Wild Card Round|Quarterfinals|Semifinals|Super Bowl")]
    [InlineData(BracketMode.Open, 14, "Wild Card Round|Quarterfinals|Semifinals|Super Bowl")]
    [InlineData(BracketMode.Open, 16, "Round of 16|Quarterfinals|Semifinals|Super Bowl")]
    [InlineData(BracketMode.Open, 32, "Round of 32|Round of 16|Quarterfinals|Semifinals|Super Bowl")]
    public void Round_names(BracketMode mode, int size, string expected)
    {
        var b = new Bracket(BracketSetup.QuickFill(mode, size));
        var names = b.Rounds.Where(r => r.Section != BracketSection.NFC).Select(r => r.Name);
        Assert.Equal(expected.Split('|'), names);
    }
}
