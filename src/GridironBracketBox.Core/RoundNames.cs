namespace GridironBracketBox.Core;

/// <summary>NFL-style display names for rounds, derived from size rather than hard-coded per bracket.</summary>
public static class RoundNames
{
    public const string Final = "Super Bowl";

    /// <param name="round">1-based round within the section.</param>
    /// <param name="games">Number of games in that round within the section.</param>
    public static string Name(BracketMode mode, int size, int round, int games)
    {
        if (round == 1 && ByeRules.ByesPerGroup(mode, size) > 0)
            return "Wild Card Round";

        if (mode == BracketMode.Conference)
        {
            return games switch
            {
                1 => "Conference Championship",
                2 => "Divisional Round",
                4 => "Wild Card Round",
                _ => "First Round",
            };
        }

        return (games * 2) switch
        {
            2 => Final,
            4 => "Semifinals",
            8 => "Quarterfinals",
            int teams => $"Round of {teams}",
        };
    }
}
