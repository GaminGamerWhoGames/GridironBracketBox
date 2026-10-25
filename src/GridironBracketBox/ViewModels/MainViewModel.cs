using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GridironBracketBox.Core;

namespace GridironBracketBox.ViewModels;

/// <summary>Owns which screen is showing, moves between them, and handles save/open.</summary>
public partial class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private readonly IFileDialogs _dialogs;
    private readonly SetupViewModel _setup;
    private int _toastId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>Short message shown at the bottom of the window ("Saved …", or an error). Null = hidden.</summary>
    [ObservableProperty]
    public partial string? Toast { get; set; }

    [ObservableProperty]
    public partial bool ToastIsError { get; set; }

    public MainViewModel(IFileDialogs dialogs)
    {
        _dialogs = dialogs;
        _setup = new SetupViewModel(StartBracket, OpenCommand);
        CurrentPage = _setup;
    }

    /// <summary>Called by the setup screen when the user clicks Start.</summary>
    public void StartBracket(BracketSetup setup) => ShowBracket(new Bracket(setup), fileName: null);

    /// <summary>Back to setup, keeping the teams/seeds the user chose.</summary>
    public void ShowSetup() => CurrentPage = _setup;

    private void ShowBracket(Bracket bracket, string? fileName) =>
        CurrentPage = new PlayViewModel(bracket, ShowSetup, SaveCommand, OpenCommand) { FileName = fileName };

    // ---- Save / Open ----------------------------------------------------------

    private bool CanSave() => CurrentPage is PlayViewModel;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (CurrentPage is not PlayViewModel play) return;
        try
        {
            string suggested = play.FileName ??
                $"bracket-{play.Bracket.Setup.Size}-{play.Bracket.Setup.Mode.ToString().ToLowerInvariant()}-{DateTime.Now:yyyy-MM-dd}.json";
            string? name = await _dialogs.SaveTextAsync(suggested, BracketFile.Serialize(play.Bracket));
            if (name is null) return; // cancelled
            play.FileName = name;
            ShowToast($"Saved {name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Couldn't save: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        OpenedFile? file;
        try
        {
            file = await _dialogs.OpenTextAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Couldn't open the file: {ex.Message}", isError: true);
            return;
        }
        if (file is null) return; // cancelled

        try
        {
            var bracket = BracketFile.Deserialize(file.Text);
            _setup.Load(bracket.Setup); // so "← Setup" shows the loaded teams
            ShowBracket(bracket, file.Name);
            ShowToast($"Opened {file.Name}");
        }
        catch (BracketFileException ex)
        {
            ShowToast($"Couldn't open {file.Name}: {ex.Message}", isError: true);
        }
    }

    /// <summary>Shows a message for a few seconds. A newer message replaces an older one.</summary>
    private async void ShowToast(string message, bool isError = false)
    {
        int id = ++_toastId;
        Toast = message;
        ToastIsError = isError;
        await Task.Delay(isError ? ToastDuration * 2 : ToastDuration);
        if (id == _toastId) Toast = null;
    }
}
