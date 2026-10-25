using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GridironBracketBox.ViewModels;

namespace GridironBracketBox.Views;

/// <summary>The real Save/Open dialogs (Windows Explorer dialogs, or the Linux desktop's own).</summary>
public sealed class AvaloniaFileDialogs(TopLevel window) : IFileDialogs
{
    private static readonly FilePickerFileType BracketFiles = new("Gridiron bracket (*.json)")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    public async Task<string?> SaveTextAsync(string suggestedName, string text)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save bracket",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [BracketFiles],
            ShowOverwritePrompt = true,
        });
        if (file is null) return null;

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0); // overwriting a longer file must not leave old text at the end
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(text);
        return file.Name;
    }

    public async Task<OpenedFile?> OpenTextAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open bracket",
            AllowMultiple = false,
            FileTypeFilter = [BracketFiles, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new OpenedFile(files[0].Name, await reader.ReadToEndAsync());
    }
}
