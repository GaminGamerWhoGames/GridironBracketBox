namespace GridironBracketBox.Core;

/// <summary>Which part of the bracket a game belongs to.</summary>
public enum BracketSection { AFC, NFC, Open, Final }

/// <summary>"In round <see cref="Round"/>, <see cref="Winner"/> beat <see cref="Loser"/>" (team abbreviations).</summary>
public sealed record Pick(int Round, string Winner, string Loser);

/// <summary>
/// One game slot. TeamA is the higher seed (or the AFC champion in the conference-mode final).
/// Teams are null while the previous round is still being played.
/// </summary>
public sealed record Game(BracketSection Section, int Round, int Index, SeededTeam? TeamA, SeededTeam? TeamB, SeededTeam? Winner)
{
    public string Id => $"{Section}-R{Round}-G{Index}";
    public bool IsReady => TeamA is not null && TeamB is not null;
    public bool IsDecided => Winner is not null;
    public SeededTeam? Loser => Winner is null ? null : Winner == TeamA ? TeamB : TeamA;
}

/// <summary>All games of one round within one section.</summary>
public sealed record Round(BracketSection Section, int Number, string Name, IReadOnlyList<Game> Games);

/// <summary>
/// A playable bracket. State is just the setup plus a list of picks; every round is
/// re-derived from those using NFL re-seeding, so all sizes share one algorithm.
/// </summary>
public sealed class Bracket
{
    private List<Pick> _picks = [];
    private readonly Stack<List<Pick>> _undo = new();

    public BracketSetup Setup { get; }
    public IReadOnlyList<Pick> Picks => _picks;

    /// <summary>Rounds in play order, grouped by section (AFC rounds, NFC rounds, then Final; or Open rounds).</summary>
    public IReadOnlyList<Round> Rounds { get; private set; } = [];
    public SeededTeam? Champion { get; private set; }
    public bool IsComplete => Champion is not null;
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Raised after any change to picks.</summary>
    public event EventHandler? Changed;

    public Bracket(BracketSetup setup, IEnumerable<Pick>? picks = null)
    {
        var errors = setup.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid bracket setup: " + string.Join(" ", errors), nameof(setup));
        Setup = setup;
        _picks = picks?.ToList() ?? [];
        Rebuild();
    }

    public IEnumerable<Game> AllGames => Rounds.SelectMany(r => r.Games);

    public Game? FindGame(string id) => AllGames.FirstOrDefault(g => g.Id == id);

    /// <summary>Teams in a section that skip round 1 (empty when there are no byes).</summary>
    public IReadOnlyList<SeededTeam> ByeTeams(BracketSection section)
    {
        int byes = ByeRules.ByesPerGroup(Setup.Mode, Setup.Size);
        return Setup.Groups()
            .Where(g => SectionFor(g.Key) == section)
            .SelectMany(g => g.Teams.Take(byes))
            .ToList();
    }

    private BracketSection SectionFor(Conference groupKey) =>
        Setup.Mode == BracketMode.Open ? BracketSection.Open
        : groupKey == Conference.AFC ? BracketSection.AFC
        : BracketSection.NFC;

    /// <summary>Marks <paramref name="winnerAbbr"/> as the winner of <paramref name="game"/>, replacing any earlier pick.</summary>
    public void SetWinner(Game game, string winnerAbbr)
    {
        var current = FindGame(game.Id) ?? throw new ArgumentException($"No game {game.Id}.", nameof(game));
        if (!current.IsReady)
            throw new InvalidOperationException($"Game {game.Id} doesn't have both teams yet.");
        var winner = current.TeamA!.Abbr == winnerAbbr ? current.TeamA
            : current.TeamB!.Abbr == winnerAbbr ? current.TeamB
            : throw new ArgumentException($"{winnerAbbr} isn't playing in game {game.Id}.", nameof(winnerAbbr));
        if (current.Winner == winner) return;

        var loser = winner == current.TeamA ? current.TeamB! : current.TeamA!;
        Mutate(picks =>
        {
            picks.RemoveAll(p => IsPickFor(p, current));
            picks.Add(new Pick(current.Round, winner.Abbr, loser.Abbr));
        });
    }

    /// <summary>Removes the pick for a game (and, through the rebuild, any later pick that depended on it).</summary>
    public void ClearWinner(Game game)
    {
        var current = FindGame(game.Id);
        if (current is null || !current.IsDecided) return;
        Mutate(picks => picks.RemoveAll(p => IsPickFor(p, current)));
    }

    /// <summary>Clears every pick. Undoable.</summary>
    public void Reset()
    {
        if (_picks.Count == 0) return;
        Mutate(picks => picks.Clear());
    }

    /// <summary>Restores the picks as they were before the last change.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _picks = _undo.Pop();
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Mutate(Action<List<Pick>> change)
    {
        _undo.Push([.. _picks]);
        change(_picks);
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPickFor(Pick p, Game g) =>
        g.IsReady && p.Round == g.Round &&
        ((p.Winner == g.TeamA!.Abbr && p.Loser == g.TeamB!.Abbr) ||
         (p.Winner == g.TeamB!.Abbr && p.Loser == g.TeamA!.Abbr));

    // ---- Derivation -------------------------------------------------------

    private void Rebuild()
    {
        var rounds = new List<Round>();
        var kept = new List<Pick>();
        var gamesPerRound = ByeRules.GamesPerRound(Setup.Mode, Setup.Size);
        int byes = ByeRules.ByesPerGroup(Setup.Mode, Setup.Size);
        var champions = new List<SeededTeam?>();

        foreach (var (conf, teams) in Setup.Groups())
        {
            var section = SectionFor(conf);
            var byeTeams = teams.Take(byes).ToList();
            List<SeededTeam>? alive = teams.Skip(byes).ToList();

            for (int r = 1; r <= gamesPerRound.Count; r++)
            {
                var games = alive is null
                    ? Placeholders(section, r, gamesPerRound[r - 1])
                    : PlayRound(section, r, ReSeed(alive), kept);
                rounds.Add(new Round(section, r, RoundNames.Name(Setup.Mode, Setup.Size, r, games.Count), games));

                // The next round is only known once every game in this one is decided.
                alive = alive is not null && games.All(g => g.IsDecided)
                    ? [.. games.Select(g => g.Winner!), .. (r == 1 ? byeTeams : [])]
                    : null;
            }
            champions.Add(alive?.Single());
        }

        if (Setup.Mode == BracketMode.Conference)
        {
            int finalRound = gamesPerRound.Count + 1;
            var games = champions.All(c => c is not null)
                ? PlayRound(BracketSection.Final, finalRound, [champions[0]!, champions[1]!], kept)
                : Placeholders(BracketSection.Final, finalRound, 1);
            rounds.Add(new Round(BracketSection.Final, finalRound, RoundNames.Final, games));
            Champion = games[0].Winner;
        }
        else
        {
            Champion = champions[0];
        }

        // Picks that no longer match any real matchup are dropped here.
        _picks = kept;
        Rounds = rounds;
    }

    /// <summary>NFL re-seeding: highest remaining seed plays the lowest, next highest the next lowest, and so on.</summary>
    private static List<SeededTeam> ReSeed(List<SeededTeam> alive) => [.. alive.OrderBy(t => t.Seed)];

    /// <summary>Pairs teams first-vs-last (already ordered), applying any pick whose exact matchup exists.</summary>
    private List<Game> PlayRound(BracketSection section, int round, List<SeededTeam> ordered, List<Pick> kept)
    {
        var games = new List<Game>();
        for (int i = 0; i < ordered.Count / 2; i++)
        {
            var a = ordered[i];
            var b = ordered[ordered.Count - 1 - i];
            var game = new Game(section, round, i, a, b, null);
            // Last matching pick wins, in case a loaded file contains duplicates.
            var pick = _picks.LastOrDefault(p => IsPickFor(p, game));
            if (pick is not null)
            {
                kept.Add(pick);
                game = game with { Winner = pick.Winner == a.Abbr ? a : b };
            }
            games.Add(game);
        }
        return games;
    }

    private static List<Game> Placeholders(BracketSection section, int round, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Game(section, round, i, null, null, null))];
}
