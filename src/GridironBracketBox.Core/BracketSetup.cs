namespace GridironBracketBox.Core;

/// <summary>A team with its seed. In conference mode the seed is within the team's conference.</summary>
public sealed record SeededTeam(Team Team, int Seed)
{
    public string Abbr => Team.Abbr;
    public override string ToString() => $"({Seed}) {Team.Abbr}";
}

/// <summary>One line of the setup: which team, which seed. Kept to plain values so it saves to JSON cleanly.</summary>
public sealed record SeedEntry(string Abbr, int Seed);

public sealed record BracketSetup(BracketMode Mode, int Size, IReadOnlyList<SeedEntry> Entries)
{
    /// <summary>Returns every problem with this setup; an empty list means it's ready to play.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!ByeRules.SupportedSizes.Contains(Size))
        {
            errors.Add($"Playoff size must be one of {string.Join(", ", ByeRules.SupportedSizes)}.");
            return errors;
        }

        if (Entries.Count != Size)
            errors.Add($"Pick exactly {Size} teams (currently {Entries.Count}).");

        foreach (var abbr in Entries.Select(e => e.Abbr).Where(a => NflTeams.Find(a) is null).Distinct())
            errors.Add($"Unknown team '{abbr}'.");

        foreach (var dup in Entries.GroupBy(e => e.Abbr, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"{dup.Key} is picked more than once.");

        if (errors.Count > 0) return errors;

        int groupSize = ByeRules.GroupSize(Mode, Size);
        foreach (var group in Groups())
        {
            string label = Mode == BracketMode.Conference ? group.Key.ToString() : "The bracket";
            if (Mode == BracketMode.Conference && group.Teams.Count != groupSize)
                errors.Add($"{label} needs exactly {groupSize} teams (currently {group.Teams.Count}).");

            var seeds = group.Teams.Select(t => t.Seed).ToList();
            foreach (var bad in seeds.Where(s => s < 1 || s > groupSize).Distinct().Order())
                errors.Add($"{label}: seed {bad} is out of range (1-{groupSize}).");
            foreach (var dup in seeds.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).Order())
                errors.Add($"{label}: seed {dup} is used more than once.");
        }
        return errors;
    }

    public bool IsValid => Validate().Count == 0;

    /// <summary>
    /// Teams split into the brackets they play in: AFC and NFC in conference mode,
    /// a single group (keyed AFC, unused) in open mode. Sorted by seed.
    /// </summary>
    internal IEnumerable<(Conference Key, IReadOnlyList<SeededTeam> Teams)> Groups()
    {
        var seeded = Entries
            .Select(e => new SeededTeam(NflTeams.Find(e.Abbr)!, e.Seed))
            .OrderBy(t => t.Seed)
            .ToList();

        if (Mode == BracketMode.Open)
        {
            yield return (Conference.AFC, seeded);
            yield break;
        }
        foreach (var conf in new[] { Conference.AFC, Conference.NFC })
            yield return (conf, seeded.Where(t => t.Team.Conference == conf).ToList());
    }

    /// <summary>
    /// A valid setup using teams in built-in list order. Handy for tests and the "auto-fill" button.
    /// </summary>
    public static BracketSetup QuickFill(BracketMode mode, int size)
    {
        IEnumerable<SeedEntry> entries = mode == BracketMode.Open
            ? NflTeams.All.Take(size).Select((t, i) => new SeedEntry(t.Abbr, i + 1))
            : new[] { Conference.AFC, Conference.NFC }.SelectMany(conf => NflTeams.All
                .Where(t => t.Conference == conf)
                .Take(size / 2)
                .Select((t, i) => new SeedEntry(t.Abbr, i + 1)));
        return new BracketSetup(mode, size, entries.ToList());
    }

    /// <summary>
    /// A valid setup with random teams in random seed order. Conference mode draws N/2 from each
    /// conference; open mode draws from all 32. Pass a seeded <see cref="Random"/> for repeatable results.
    /// </summary>
    public static BracketSetup Randomize(BracketMode mode, int size, Random? rng = null)
    {
        rng ??= Random.Shared;
        IEnumerable<IEnumerable<Team>> pools = mode == BracketMode.Open
            ? [NflTeams.All]
            : [NflTeams.All.Where(t => t.Conference == Conference.AFC), NflTeams.All.Where(t => t.Conference == Conference.NFC)];
        int perPool = ByeRules.GroupSize(mode, size);

        var entries = pools.SelectMany(pool => pool
            .OrderBy(_ => rng.Next())
            .Take(perPool)
            .Select((t, i) => new SeedEntry(t.Abbr, i + 1)));
        return new BracketSetup(mode, size, entries.ToList());
    }
}
