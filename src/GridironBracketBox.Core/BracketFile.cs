using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridironBracketBox.Core;

/// <summary>Thrown when a file can't be loaded as a bracket. The message is written for the user.</summary>
public sealed class BracketFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Saves and loads brackets as readable JSON. Only the setup and the picks are stored;
/// everything else (rounds, matchups, champion) is re-derived when the file is loaded.
/// </summary>
public static class BracketFile
{
    public const string Format = "gridiron-bracket-box";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }, // "Conference" instead of 0
    };

    /// <summary>Exactly what goes in the file. Bump <see cref="CurrentVersion"/> if this shape changes.</summary>
    private sealed record FileContents(
        string? Format,
        int Version,
        DateTimeOffset SavedAt,
        BracketMode Mode,
        int Size,
        List<SeedEntry>? Teams,
        List<Pick>? Picks);

    public static string Serialize(Bracket bracket, DateTimeOffset? savedAt = null) =>
        JsonSerializer.Serialize(
            new FileContents(
                Format,
                CurrentVersion,
                savedAt ?? DateTimeOffset.Now,
                bracket.Setup.Mode,
                bracket.Setup.Size,
                [.. bracket.Setup.Entries.OrderBy(e => NflTeams.Find(e.Abbr)?.Conference).ThenBy(e => e.Seed)],
                [.. bracket.Picks]),
            Options);

    /// <summary>Rebuilds a bracket from file text, or throws <see cref="BracketFileException"/> with a readable reason.</summary>
    public static Bracket Deserialize(string json)
    {
        FileContents? file;
        try
        {
            file = JsonSerializer.Deserialize<FileContents>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BracketFileException("This file isn't a bracket file (it couldn't be read as JSON).", ex);
        }

        if (file is null || file.Format != Format)
            throw new BracketFileException("This file isn't a Gridiron Bracket Box file.");
        if (file.Version > CurrentVersion)
            throw new BracketFileException("This file was saved by a newer version of Gridiron Bracket Box.");
        if (file.Teams is null)
            throw new BracketFileException("This file has no teams in it.");

        var setup = new BracketSetup(file.Mode, file.Size, file.Teams);
        var errors = setup.Validate();
        if (errors.Count > 0)
            throw new BracketFileException("The bracket in this file isn't valid: " + string.Join(" ", errors));

        // Any pick that doesn't match a real matchup is dropped by the Bracket itself.
        return new Bracket(setup, file.Picks ?? []);
    }
}
