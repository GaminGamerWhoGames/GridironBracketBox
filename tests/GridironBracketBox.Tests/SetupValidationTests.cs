using GridironBracketBox.Core;

namespace GridironBracketBox.Tests;

public class SetupValidationTests
{
    [Fact]
    public void There_are_32_teams_16_per_conference()
    {
        Assert.Equal(32, NflTeams.All.Count);
        Assert.Equal(32, NflTeams.All.Select(t => t.Abbr).Distinct().Count());
        Assert.Equal(16, NflTeams.All.Count(t => t.Conference == Conference.AFC));
    }

    [Theory]
    [MemberData(nameof(ByeRulesTests.AllConfigs), MemberType = typeof(ByeRulesTests))]
    public void QuickFill_is_valid(BracketMode mode, int size) =>
        Assert.Empty(BracketSetup.QuickFill(mode, size).Validate());

    [Theory]
    [MemberData(nameof(ByeRulesTests.AllConfigs), MemberType = typeof(ByeRulesTests))]
    public void Randomize_is_always_valid_and_playable(BracketMode mode, int size)
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var setup = BracketSetup.Randomize(mode, size, new Random(seed));
            Assert.Empty(setup.Validate());

            var bracket = new Bracket(setup);
            FullTournamentTests.PlayOut(bracket, g => g.TeamA!);
            Assert.True(bracket.IsComplete);
        }
    }

    [Theory]
    [MemberData(nameof(ByeRulesTests.AllConfigs), MemberType = typeof(ByeRulesTests))]
    public void Randomize_actually_varies(BracketMode mode, int size)
    {
        var distinct = Enumerable.Range(0, 20)
            .Select(seed => string.Join(",", BracketSetup.Randomize(mode, size, new Random(seed)).Entries))
            .Distinct()
            .Count();
        Assert.True(distinct > 1, "20 randomized setups were all identical.");
    }

    [Fact]
    public void Randomize_is_repeatable_with_same_seed() =>
        Assert.Equal(
            BracketSetup.Randomize(BracketMode.Open, 14, new Random(42)).Entries,
            BracketSetup.Randomize(BracketMode.Open, 14, new Random(42)).Entries);

    [Fact]
    public void Wrong_team_count()
    {
        var setup = BracketSetup.QuickFill(BracketMode.Open, 8);
        setup = setup with { Entries = setup.Entries.Take(7).ToList() };
        Assert.Contains(setup.Validate(), e => e.Contains("exactly 8 teams"));
    }

    [Fact]
    public void Unsupported_size() =>
        Assert.NotEmpty(new BracketSetup(BracketMode.Open, 10, []).Validate());

    [Fact]
    public void Duplicate_team()
    {
        var entries = BracketSetup.QuickFill(BracketMode.Open, 8).Entries.ToList();
        entries[7] = new SeedEntry(entries[0].Abbr, 8);
        Assert.Contains(new BracketSetup(BracketMode.Open, 8, entries).Validate(), e => e.Contains("more than once"));
    }

    [Fact]
    public void Duplicate_seed()
    {
        var entries = BracketSetup.QuickFill(BracketMode.Open, 8).Entries.ToList();
        entries[7] = entries[7] with { Seed = 1 };
        Assert.Contains(new BracketSetup(BracketMode.Open, 8, entries).Validate(), e => e.Contains("seed 1 is used more than once"));
    }

    [Fact]
    public void Seed_out_of_range_in_conference_mode()
    {
        var entries = BracketSetup.QuickFill(BracketMode.Conference, 8).Entries.ToList();
        entries[0] = entries[0] with { Seed = 5 }; // AFC only has seeds 1-4
        Assert.Contains(new BracketSetup(BracketMode.Conference, 8, entries).Validate(), e => e.Contains("seed 5 is out of range"));
    }

    [Fact]
    public void Conference_mode_needs_even_split()
    {
        // 8 AFC teams, 0 NFC teams.
        var entries = NflTeams.All.Where(t => t.Conference == Conference.AFC).Take(8)
            .Select((t, i) => new SeedEntry(t.Abbr, i % 4 + 1)).ToList();
        var errors = new BracketSetup(BracketMode.Conference, 8, entries).Validate();
        Assert.Contains(errors, e => e.StartsWith("AFC needs exactly 4"));
        Assert.Contains(errors, e => e.StartsWith("NFC needs exactly 4"));
    }

    [Fact]
    public void Open_mode_ignores_conference()
    {
        var entries = NflTeams.All.Where(t => t.Conference == Conference.NFC).Take(8)
            .Select((t, i) => new SeedEntry(t.Abbr, i + 1)).ToList();
        Assert.Empty(new BracketSetup(BracketMode.Open, 8, entries).Validate());
    }

    [Fact]
    public void Bracket_rejects_invalid_setup() =>
        Assert.Throws<ArgumentException>(() => new Bracket(new BracketSetup(BracketMode.Open, 8, [])));
}
