namespace GridironBracketBox.Core;

public enum Conference { AFC, NFC }

public sealed record Team(string Abbr, string Name, Conference Conference)
{
    public override string ToString() => $"{Abbr} {Name}";
}

/// <summary>The built-in list of all 32 NFL teams.</summary>
public static class NflTeams
{
    public static readonly IReadOnlyList<Team> All =
    [
        // AFC
        new("BUF", "Buffalo Bills", Conference.AFC),
        new("MIA", "Miami Dolphins", Conference.AFC),
        new("NE", "New England Patriots", Conference.AFC),
        new("NYJ", "New York Jets", Conference.AFC),
        new("BAL", "Baltimore Ravens", Conference.AFC),
        new("CIN", "Cincinnati Bengals", Conference.AFC),
        new("CLE", "Cleveland Browns", Conference.AFC),
        new("PIT", "Pittsburgh Steelers", Conference.AFC),
        new("HOU", "Houston Texans", Conference.AFC),
        new("IND", "Indianapolis Colts", Conference.AFC),
        new("JAX", "Jacksonville Jaguars", Conference.AFC),
        new("TEN", "Tennessee Titans", Conference.AFC),
        new("DEN", "Denver Broncos", Conference.AFC),
        new("KC", "Kansas City Chiefs", Conference.AFC),
        new("LV", "Las Vegas Raiders", Conference.AFC),
        new("LAC", "Los Angeles Chargers", Conference.AFC),
        // NFC
        new("DAL", "Dallas Cowboys", Conference.NFC),
        new("NYG", "New York Giants", Conference.NFC),
        new("PHI", "Philadelphia Eagles", Conference.NFC),
        new("WAS", "Washington Commanders", Conference.NFC),
        new("CHI", "Chicago Bears", Conference.NFC),
        new("DET", "Detroit Lions", Conference.NFC),
        new("GB", "Green Bay Packers", Conference.NFC),
        new("MIN", "Minnesota Vikings", Conference.NFC),
        new("ATL", "Atlanta Falcons", Conference.NFC),
        new("CAR", "Carolina Panthers", Conference.NFC),
        new("NO", "New Orleans Saints", Conference.NFC),
        new("TB", "Tampa Bay Buccaneers", Conference.NFC),
        new("ARI", "Arizona Cardinals", Conference.NFC),
        new("LAR", "Los Angeles Rams", Conference.NFC),
        new("SF", "San Francisco 49ers", Conference.NFC),
        new("SEA", "Seattle Seahawks", Conference.NFC),
    ];

    private static readonly Dictionary<string, Team> ByAbbr =
        All.ToDictionary(t => t.Abbr, StringComparer.OrdinalIgnoreCase);

    public static Team? Find(string abbr) => ByAbbr.GetValueOrDefault(abbr);
}
