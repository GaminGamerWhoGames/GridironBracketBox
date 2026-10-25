using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GridironBracketBox.Core;

namespace GridironBracketBox.ViewModels;

/// <summary>
/// Play screen. Rebuilds its column/card view models from the Core <see cref="Core.Bracket"/>
/// after every change, so the screen can never drift out of sync with the bracket logic.
/// </summary>
public partial class PlayViewModel : ViewModelBase
{
    /// <summary>Vertical space per card, used to size columns so later rounds center between earlier ones.</summary>
    private const double CardSlotHeight = 96;

    private readonly Action _backToSetup;

    public Bracket Bracket { get; }

    public string Title { get; }

    /// <summary>Columns left to right. Conference: AFC rounds → Super Bowl → NFC rounds (mirrored).</summary>
    public ObservableCollection<RoundColumnViewModel> Columns { get; } = [];

    [ObservableProperty]
    public partial string Progress { get; set; } = "";

    [ObservableProperty]
    public partial string? ChampionText { get; set; }

    /// <summary>One-line feedback, e.g. when a changed pick cleared later picks.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>Shrink the whole bracket to fit the window (off = full size with scroll bars).</summary>
    [ObservableProperty]
    public partial bool FitToWindow { get; set; } = true;

    /// <summary>Name of the file this bracket was last saved to / opened from (null = never saved).</summary>
    [ObservableProperty]
    public partial string? FileName { get; set; }

    /// <summary>Save… and Open… (shared with the setup screen and Ctrl+S / Ctrl+O).</summary>
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }

    public PlayViewModel(Bracket bracket, Action backToSetup, ICommand saveCommand, ICommand openCommand)
    {
        Bracket = bracket;
        _backToSetup = backToSetup;
        SaveCommand = saveCommand;
        OpenCommand = openCommand;
        Title = $"{bracket.Setup.Size}-team {(bracket.Setup.Mode == BracketMode.Conference ? "Conference" : "Open")} bracket";
        Bracket.Changed += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        List<RoundColumnViewModel> columns = Bracket.Setup.Mode == BracketMode.Conference
            ? [.. SectionColumns(BracketSection.AFC), .. SectionColumns(BracketSection.Final), .. SectionColumns(BracketSection.NFC).Reverse()]
            : SectionColumns(BracketSection.Open).ToList();

        // Every column gets the same height; UniformGrid then spreads each column's cards evenly,
        // which centers round-2 games between round-1 games, and so on.
        double height = columns.Max(c => c.Cards.Count) * CardSlotHeight;
        Columns.Clear();
        foreach (var column in columns)
        {
            column.Height = height;
            Columns.Add(column);
        }

        int decided = Bracket.AllGames.Count(g => g.IsDecided);
        int total = Bracket.AllGames.Count();
        Progress = $"{decided} of {total} games picked";
        ChampionText = Bracket.Champion is { } c ? $"({c.Seed}) {c.Team.Abbr}  {c.Team.Name}" : null;

        UndoCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<RoundColumnViewModel> SectionColumns(BracketSection section)
    {
        var byes = Bracket.ByeTeams(section);
        foreach (var round in Bracket.Rounds.Where(r => r.Section == section))
        {
            var cards = new List<GameCardViewModel>();
            if (round.Number == 1)
                cards.AddRange(byes.Select(t => GameCardViewModel.ForBye(t)));
            cards.AddRange(round.Games.Select(g => new GameCardViewModel(g, Toggle)));

            string label = section switch
            {
                BracketSection.AFC => "AFC",
                BracketSection.NFC => "NFC",
                BracketSection.Final => "AFC vs NFC",
                _ => "",
            };
            yield return new RoundColumnViewModel(round.Name, label, section == BracketSection.Final || IsLastOpenRound(round), cards);
        }
    }

    private bool IsLastOpenRound(Round round) =>
        round.Section == BracketSection.Open && round.Number == Bracket.Rounds.Max(r => r.Number);

    /// <summary>
    /// Click handler for a team: picks it, or clears the pick if it's already the winner.
    /// Reports how many later picks were cleared because they depended on this game.
    /// </summary>
    private void Toggle(Game game, SeededTeam team)
    {
        int before = Bracket.Picks.Count;
        int expected;
        if (game.Winner == team)
        {
            Bracket.ClearWinner(game);
            expected = before - 1;
        }
        else
        {
            expected = game.IsDecided ? before : before + 1;
            Bracket.SetWinner(game, team.Abbr);
        }

        int cleared = expected - Bracket.Picks.Count;
        Notice = cleared > 0
            ? $"That change cleared {cleared} later pick{(cleared == 1 ? "" : "s")} that depended on it. Press Undo to bring {(cleared == 1 ? "it" : "them")} back."
            : "";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Bracket.Undo();
        Notice = "";
    }

    private bool CanUndo() => Bracket.CanUndo;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        Bracket.Reset();
        Notice = "Bracket reset. Press Undo to restore your picks.";
    }

    private bool CanReset() => Bracket.Picks.Count > 0;

    [RelayCommand]
    private void BackToSetup() => _backToSetup();
}

/// <summary>One round in one section, drawn as a column of cards.</summary>
public sealed class RoundColumnViewModel
{
    public RoundColumnViewModel(string title, string sectionLabel, bool isTitleGame, IReadOnlyList<GameCardViewModel> cards)
    {
        Title = title;
        SectionLabel = sectionLabel;
        Cards = cards;
        foreach (var card in cards) card.IsTitleGame = isTitleGame;
    }

    public string Title { get; }
    public string SectionLabel { get; }
    public IReadOnlyList<GameCardViewModel> Cards { get; }
    public double Height { get; set; }
}

/// <summary>A game (two team lines) or a bye (one team line, no opponent).</summary>
public sealed class GameCardViewModel
{
    public TeamSlotViewModel Top { get; }
    public TeamSlotViewModel Bottom { get; }
    public bool IsBye { get; }

    /// <summary>The championship game gets a gold border.</summary>
    public bool IsTitleGame { get; set; }

    public GameCardViewModel(Game game, Action<Game, SeededTeam> toggle)
    {
        Top = new TeamSlotViewModel(game, game.TeamA, toggle);
        Bottom = new TeamSlotViewModel(game, game.TeamB, toggle);
    }

    private GameCardViewModel(SeededTeam byeTeam)
    {
        IsBye = true;
        Top = TeamSlotViewModel.ForBye(byeTeam);
        Bottom = TeamSlotViewModel.ForBye(null);
    }

    public static GameCardViewModel ForBye(SeededTeam team) => new(team);
}

/// <summary>One team line on a card. Clicking it picks (or un-picks) that team.</summary>
public sealed class TeamSlotViewModel
{
    private readonly SeededTeam? _team;

    public TeamSlotViewModel(Game? game, SeededTeam? team, Action<Game, SeededTeam>? toggle)
    {
        _team = team;
        IsWinner = game?.Winner is not null && game.Winner == team;
        IsLoser = game?.Winner is not null && team is not null && game.Winner != team;
        bool canPick = game is { IsReady: true } && team is not null && toggle is not null;
        PickCommand = new RelayCommand(() => toggle!(game!, team!), () => canPick);
        Tip = !canPick ? null
            : IsWinner ? $"{team!.Abbr} is the winner. Click again to clear this pick."
            : $"Pick {team!.Abbr} to win";
    }

    public static TeamSlotViewModel ForBye(SeededTeam? team) => new(null, team, null);

    public bool IsEmpty => _team is null;
    public string SeedText => _team is null ? "" : _team.Seed.ToString();
    public string Abbr => _team?.Team.Abbr ?? "TBD";
    public string Name => _team?.Team.Name ?? "";
    public bool IsAfc => _team?.Team.Conference == Conference.AFC;
    public bool IsWinner { get; }
    public bool IsLoser { get; }
    public string? Tip { get; }
    public IRelayCommand PickCommand { get; }
}
