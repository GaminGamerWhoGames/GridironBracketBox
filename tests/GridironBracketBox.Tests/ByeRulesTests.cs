using GridironBracketBox.Core;

namespace GridironBracketBox.Tests;

public class ByeRulesTests
{
    /// <summary>Every supported size in both modes — used by tests across the suite.</summary>
    public static TheoryData<BracketMode, int> AllConfigs()
    {
        var data = new TheoryData<BracketMode, int>();
        foreach (var mode in Enum.GetValues<BracketMode>())
            foreach (var size in ByeRules.SupportedSizes)
                data.Add(mode, size);
        return data;
    }

    [Theory]
    [InlineData(BracketMode.Conference, 8, 0)]
    [InlineData(BracketMode.Conference, 12, 2)]
    [InlineData(BracketMode.Conference, 14, 1)]
    [InlineData(BracketMode.Conference, 16, 0)]
    [InlineData(BracketMode.Conference, 32, 0)]
    [InlineData(BracketMode.Open, 8, 0)]
    [InlineData(BracketMode.Open, 12, 4)]
    [InlineData(BracketMode.Open, 14, 2)]
    [InlineData(BracketMode.Open, 16, 0)]
    [InlineData(BracketMode.Open, 32, 0)]
    public void Byes_match_spec(BracketMode mode, int size, int expectedByes) =>
        Assert.Equal(expectedByes, ByeRules.ByesPerGroup(mode, size));

    [Theory]
    [MemberData(nameof(AllConfigs))]
    public void Every_rule_produces_a_valid_bracket(BracketMode mode, int size) =>
        Assert.Null(ByeRules.Validate(mode, size));

    [Theory]
    [InlineData(BracketMode.Conference, 8, new[] { 2, 1 })]
    [InlineData(BracketMode.Conference, 12, new[] { 2, 2, 1 })]
    [InlineData(BracketMode.Conference, 14, new[] { 3, 2, 1 })]
    [InlineData(BracketMode.Conference, 16, new[] { 4, 2, 1 })]
    [InlineData(BracketMode.Conference, 32, new[] { 8, 4, 2, 1 })]
    [InlineData(BracketMode.Open, 8, new[] { 4, 2, 1 })]
    [InlineData(BracketMode.Open, 12, new[] { 4, 4, 2, 1 })]
    [InlineData(BracketMode.Open, 14, new[] { 6, 4, 2, 1 })]
    [InlineData(BracketMode.Open, 16, new[] { 8, 4, 2, 1 })]
    [InlineData(BracketMode.Open, 32, new[] { 16, 8, 4, 2, 1 })]
    public void Games_per_round(BracketMode mode, int size, int[] expected) =>
        Assert.Equal(expected, ByeRules.GamesPerRound(mode, size));

    [Fact]
    public void Unsupported_size_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ByeRules.ByesPerGroup(BracketMode.Open, 10));
}
