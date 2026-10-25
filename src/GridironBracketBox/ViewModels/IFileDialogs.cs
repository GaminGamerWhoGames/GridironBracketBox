using System.Threading.Tasks;

namespace GridironBracketBox.ViewModels;

/// <summary>A file the user picked to open: its display name and its text.</summary>
public sealed record OpenedFile(string Name, string Text);

/// <summary>
/// What the view models need from the OS file dialogs. Kept as an interface so view models
/// don't depend on Avalonia's windowing (the real implementation is Views/AvaloniaFileDialogs).
/// </summary>
public interface IFileDialogs
{
    /// <summary>Asks where to save, writes the text, and returns the file's name; null if the user cancelled.</summary>
    Task<string?> SaveTextAsync(string suggestedName, string text);

    /// <summary>Asks which file to open and reads it; null if the user cancelled.</summary>
    Task<OpenedFile?> OpenTextAsync();
}
