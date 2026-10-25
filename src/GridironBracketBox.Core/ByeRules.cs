namespace GridironBracketBox.Core;

public enum BracketMode
{
    /// <summary>AFC and NFC each run a bracket; the two champions meet in the final.</summary>
    Conference,
    /// <summary>One bracket, seeds 1..N, conference ignored.</summary>
    Open,
}

/// <summary>
/// THE place to change bye rules. Everything else (round counts, matchups, names)
/// is derived from this table.
/// </summary>
public static class ByeRules
{
    public static readonly IReadOnlyList<int> SupportedSizes = [8, 12, 14, 16, 32];

    // Number of top seeds that skip round 1.
    // Conference mode: byes PER CONFERENCE (seeds 1..byes in AFC and in NFC).
    // Open mode: byes in the single bracket (seeds 1..byes).
    private static readonly Dictionary<(BracketMode Mode, int Size), int> Byes = new()
    {
        [(BracketMode.Conference, 8)] = 0,
        [(BracketMode.Conference, 12)] = 2,
        [(BracketMode.Conference, 14)] = 1,
        [(BracketMode.Conference, 16)] = 0,
        [(BracketMode.Conference, 32)] = 0,

        [(BracketMode.Open, 8)] = 0,
        [(BracketMode.Open, 12)] = 4,
        [(BracketMode.Open, 14)] = 2,
        [(BracketMode.Open, 16)] = 0,
        [(BracketMode.Open, 32)] = 0,
    };

    /// <summary>Top seeds per group (conference, or the whole open bracket) that get a bye.</summary>
    public static int ByesPerGroup(BracketMode mode, int size) =>
        Byes.TryGetValue((mode, size), out var byes)
            ? byes
            : throw new ArgumentOutOfRangeException(nameof(size), $"Unsupported playoff size {size} for {mode} mode.");

    /// <summary>Teams per group: half the field in conference mode, the whole field in open mode.</summary>
    public static int GroupSize(BracketMode mode, int size) =>
        mode == BracketMode.Conference ? size / 2 : size;

    /// <summary>
    /// Game counts per round within one group, e.g. conference 14 → [3, 2, 1].
    /// Round 1 is played by the non-bye teams; bye teams join in round 2.
    /// </summary>
    public static IReadOnlyList<int> GamesPerRound(BracketMode mode, int size)
    {
        int teams = GroupSize(mode, size);
        int byes = ByesPerGroup(mode, size);
        var games = new List<int> { (teams - byes) / 2 };
        int alive = games[0] + byes;
        while (alive > 1)
        {
            games.Add(alive / 2);
            alive /= 2;
        }
        return games;
    }

    /// <summary>
    /// Checks that a rule produces a real bracket: round 1 has an even number of teams
    /// and every round after it has a power-of-two field. Returns null when valid.
    /// </summary>
    public static string? Validate(BracketMode mode, int size)
    {
        int teams = GroupSize(mode, size);
        int byes = ByesPerGroup(mode, size);
        if (byes < 0 || byes >= teams) return $"{mode} {size}: byes must be between 0 and {teams - 1}.";
        if ((teams - byes) % 2 != 0) return $"{mode} {size}: round 1 has an odd number of teams.";
        int afterRound1 = (teams - byes) / 2 + byes;
        bool powerOfTwo = (afterRound1 & (afterRound1 - 1)) == 0;
        if (!powerOfTwo) return $"{mode} {size}: {afterRound1} teams remain after round 1 (must be a power of two).";
        return null;
    }
}
