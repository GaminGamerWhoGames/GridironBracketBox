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
/// Setup screen: choose size and mode, pick teams, order them into seeds.
/// A team's position in its seed list IS its seed, so seeds are always 1..N with no gaps.
/// </summary>
public partial class SetupViewModel : ViewModelBase
{
    private readonly Action<BracketSetup> _start;

    public IReadOnlyList<int> Sizes => ByeRules.SupportedSizes;

    [ObservableProperty]
    public partial int SelectedSize { get; set; }

    [ObservableProperty]
    public partial bool IsConferenceMode { get; set; }

    /// <summary>Mirror of IsConferenceMode so each radio button can bind to its own bool.</summary>
    public bool IsOpenMode
    {
        get => !IsConferenceMode;
        set => IsConferenceMode = !value;
    }

    /// <summary>Teams to pick from: an AFC column and an NFC column.</summary>
    public IReadOnlyList<TeamColumnViewModel> Columns { get; }

    /// <summary>Seed lists: AFC and NFC in conference mode, one list in open mode.</summary>
    public ObservableCollection<SeedGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial string ByeSummary { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsReady { get; set; }

    /// <summary>"Open saved bracket…" (shared with the play screen and Ctrl+O).</summary>
    public ICommand OpenCommand { get; }

    public SetupViewModel(Action<BracketSetup> start, ICommand openCommand)
    {
        _start = start;
        OpenCommand = openCommand;
        Columns =
        [
            new TeamColumnViewModel("AFC", NflTeams.All.Where(t => t.Conference == Conference.AFC).Select(t => new TeamOptionViewModel(t, this)).ToList()),
            new TeamColumnViewModel("NFC", NflTeams.All.Where(t => t.Conference == Conference.NFC).Select(t => new TeamOptionViewModel(t, this)).ToList()),
        ];
        SelectedSize = 14;
        IsConferenceMode = true;
    }

    // A new size starts with empty seed lists.
    partial void OnSelectedSizeChanged(int value) => RebuildGroups(keepPicks: false);

    partial void OnIsConferenceModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOpenMode));
        RebuildGroups(keepPicks: true);
    }

    /// <summary>
    /// Recreates the seed lists for the current size/mode. With <paramref name="keepPicks"/>, carries over
    /// the user's picks in seed order (conference → open interleaves AFC1, NFC1, AFC2, ...; extra teams are
    /// dropped from the bottom).
    /// </summary>
    private void RebuildGroups(bool keepPicks)
    {
        if (SelectedSize == 0) return; // constructor hasn't finished setting defaults

        List<Team> picked = !keepPicks ? []
            : Groups.Count == 2 ? Interleave(Groups[0].Teams, Groups[1].Teams)
            : Groups.SelectMany(g => g.Teams).ToList();

        int capacity = ByeRules.GroupSize(Mode, SelectedSize);
        int byes = ByeRules.ByesPerGroup(Mode, SelectedSize);
        Groups.Clear();
        if (IsConferenceMode)
        {
            foreach (var conf in new[] { Conference.AFC, Conference.NFC })
                Groups.Add(new SeedGroupViewModel(conf.ToString(), conf, capacity, byes, picked.Where(t => t.Conference == conf), this));
        }
        else
        {
            Groups.Add(new SeedGroupViewModel("Seeds", null, capacity, byes, picked, this));
        }

        ByeSummary = byes == 0
            ? "No byes: every team plays in round 1."
            : (byes == 1 ? "Seed 1" : $"Seeds 1–{byes}") +
              (IsConferenceMode ? " in each conference" : "") +
              (byes == 1 ? " gets" : " get") + " a first-round bye.";
        Refresh();
    }

    private static List<Team> Interleave(IEnumerable<Team> a, IEnumerable<Team> b)
    {
        var result = new List<Team>();
        using var ea = a.GetEnumerator();
        using var eb = b.GetEnumerator();
        bool moreA = true, moreB = true;
        while (moreA || moreB)
        {
            if (moreA && (moreA = ea.MoveNext())) result.Add(ea.Current);
            if (moreB && (moreB = eb.MoveNext())) result.Add(eb.Current);
        }
        return result;
    }

    private BracketMode Mode => IsConferenceMode ? BracketMode.Conference : BracketMode.Open;

    /// <summary>The seed list a team would go into.</summary>
    private SeedGroupViewModel TargetGroup(Team team) =>
        IsConferenceMode ? Groups.Single(g => g.Conference == team.Conference) : Groups[0];

    internal void Add(Team team)
    {
        var group = TargetGroup(team);
        if (group.IsFull || IsPicked(team)) return;
        group.Add(team);
    }

    /// <summary>Drag-and-drop from the picker: puts a team at a specific seed. False if it can't go there.</summary>
    internal bool TryInsert(Team team, int index)
    {
        var group = TargetGroup(team);
        if (group.IsFull || IsPicked(team)) return false;
        group.Insert(team, index);
        return true;
    }

    /// <summary>The seed list a dragged team would land in (its conference's list, or the open list).</summary>
    internal SeedGroupViewModel GroupFor(Team team) => TargetGroup(team);

    private bool IsPicked(Team team) => Groups.Any(g => g.Teams.Contains(team));

    /// <summary>Re-evaluates everything derived from the picks: availability, counts, status.</summary>
    internal void Refresh()
    {
        foreach (var option in Columns.SelectMany(c => c.Teams))
        {
            option.IsPicked = IsPicked(option.Team);
            option.IsAvailable = !option.IsPicked && !TargetGroup(option.Team).IsFull;
        }

        var setup = BuildSetup();
        var errors = setup.Validate();
        IsReady = errors.Count == 0;
        int remaining = SelectedSize - setup.Entries.Count;
        Status = IsReady
            ? $"Ready! {SelectedSize} teams seeded. Click Start Bracket."
            : remaining > 0
                ? $"Pick {remaining} more team{(remaining == 1 ? "" : "s")}" +
                  (IsConferenceMode ? $" ({string.Join(", ", Groups.Where(g => !g.IsFull).Select(g => $"{g.Capacity - g.Slots.Count} {g.Title}"))})." : ".")
                : string.Join(" ", errors);
    }

    private BracketSetup BuildSetup() => new(
        Mode,
        SelectedSize,
        Groups.SelectMany(g => g.Slots.Select(s => new SeedEntry(s.Team.Abbr, s.Seed))).ToList());

    /// <summary>Fills empty seed spots with unpicked teams in list order (keeps existing picks).</summary>
    [RelayCommand]
    private void QuickFill()
    {
        foreach (var team in NflTeams.All.Where(t => !IsPicked(t)))
        {
            var group = TargetGroup(team);
            if (!group.IsFull) group.Add(team, refresh: false);
        }
        Refresh();
    }

    /// <summary>Replaces all picks with random teams in random seed order for the current size and mode.</summary>
    [RelayCommand]
    private void Randomize() => Load(BracketSetup.Randomize(Mode, SelectedSize));

    /// <summary>Shows a complete setup (random, or loaded from a file): mode, size, teams and seeds.</summary>
    internal void Load(BracketSetup setup)
    {
        IsConferenceMode = setup.Mode == BracketMode.Conference;
        SelectedSize = setup.Size;
        foreach (var group in Groups) group.Slots.Clear();
        foreach (var entry in setup.Entries.OrderBy(e => e.Seed))
        {
            var team = NflTeams.Find(entry.Abbr)!;
            TargetGroup(team).Add(team, refresh: false);
        }
        Refresh();
    }

    /// <summary>Randomly reorders seeds within each list.</summary>
    [RelayCommand]
    private void Shuffle()
    {
        foreach (var group in Groups) group.Shuffle();
        Refresh();
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var group in Groups) group.Slots.Clear();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void Start() => _start(BuildSetup());
}

/// <summary>A column of pickable teams (AFC or NFC).</summary>
public sealed class TeamColumnViewModel(string title, IReadOnlyList<TeamOptionViewModel> teams)
{
    public string Title { get; } = title;
    public bool IsAfc => Title == "AFC";
    public IReadOnlyList<TeamOptionViewModel> Teams { get; } = teams;
}

/// <summary>One team in the picker. Clicking it adds the team to the next open seed.</summary>
public partial class TeamOptionViewModel(Team team, SetupViewModel owner) : ViewModelBase
{
    public Team Team { get; } = team;
    public bool IsAfc => Team.Conference == Conference.AFC;

    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    [RelayCommand]
    private void Add() => owner.Add(Team);
}

/// <summary>One seed list. Position in <see cref="Slots"/> = seed.</summary>
public partial class SeedGroupViewModel : ViewModelBase
{
    private readonly SetupViewModel _owner;

    public string Title { get; }
    public Conference? Conference { get; }
    public int Capacity { get; }
    public int Byes { get; }
    public ObservableCollection<SeedSlotViewModel> Slots { get; } = [];

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    public SeedGroupViewModel(string title, Conference? conference, int capacity, int byes, IEnumerable<Team> teams, SetupViewModel owner)
    {
        Title = title;
        Conference = conference;
        Capacity = capacity;
        Byes = byes;
        _owner = owner;
        foreach (var team in teams.Take(capacity))
            Slots.Add(new SeedSlotViewModel(team, this));
        Slots.CollectionChanged += (_, _) => Renumber();
        Renumber();
    }

    public bool IsFull => Slots.Count >= Capacity;
    public IEnumerable<Team> Teams => Slots.Select(s => s.Team);

    internal void Add(Team team, bool refresh = true)
    {
        Slots.Add(new SeedSlotViewModel(team, this));
        if (refresh) _owner.Refresh();
    }

    /// <summary>Adds a team at a specific position (0 = seed 1); clamps to the end of the list.</summary>
    internal void Insert(Team team, int index)
    {
        Slots.Insert(Math.Clamp(index, 0, Slots.Count), new SeedSlotViewModel(team, this));
        _owner.Refresh();
    }

    /// <summary>Drag-to-reorder: moves a slot to a specific position.</summary>
    internal void MoveTo(SeedSlotViewModel slot, int index)
    {
        int from = Slots.IndexOf(slot), to = Math.Clamp(index, 0, Slots.Count - 1);
        if (from < 0 || from == to) return;
        Slots.Move(from, to);
        _owner.Refresh();
    }

    internal void Remove(SeedSlotViewModel slot)
    {
        Slots.Remove(slot);
        _owner.Refresh();
    }

    internal void Move(SeedSlotViewModel slot, int delta)
    {
        int from = Slots.IndexOf(slot), to = from + delta;
        if (to < 0 || to >= Slots.Count) return;
        Slots.Move(from, to);
        _owner.Refresh();
    }

    internal void Shuffle()
    {
        var shuffled = Slots.OrderBy(_ => Random.Shared.Next()).ToList();
        Slots.Clear();
        foreach (var slot in shuffled) Slots.Add(slot);
    }

    /// <summary>Keeps every slot's seed number and bye flag in sync with its position.</summary>
    private void Renumber()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            Slots[i].Seed = i + 1;
            Slots[i].HasBye = i < Byes;
            Slots[i].IsFirst = i == 0;
            Slots[i].IsLast = i == Slots.Count - 1;
        }
        CountText = $"{Slots.Count} / {Capacity}";
    }
}

/// <summary>One row in a seed list.</summary>
public partial class SeedSlotViewModel(Team team, SeedGroupViewModel group) : ViewModelBase
{
    public Team Team { get; } = team;
    public bool IsAfc => Team.Conference == Conference.AFC;
    internal SeedGroupViewModel Group => group;

    /// <summary>True while this row is being dragged (highlights it).</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    /// <summary>Only open-mode lists mix conferences, so only they need the AFC/NFC tag.</summary>
    public bool ShowConference => group.Conference is null;

    [ObservableProperty]
    public partial int Seed { get; set; }

    [ObservableProperty]
    public partial bool HasBye { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool IsFirst { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool IsLast { get; set; }

    private bool CanMoveUp() => !IsFirst;
    private bool CanMoveDown() => !IsLast;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => group.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => group.Move(this, +1);

    [RelayCommand]
    private void Remove() => group.Remove(this);
}
