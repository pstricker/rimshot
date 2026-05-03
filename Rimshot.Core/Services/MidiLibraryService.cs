using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Rimshot.Core.Models;

namespace Rimshot.Core.Services;

public class MidiLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _rootDir;
    private readonly string _libraryDir;
    private readonly string _manifestPath;

    public ObservableCollection<MidiLibraryEntry> Entries { get; } = new();

    public MidiLibraryService() : this(DefaultRootDir()) { }

    public MidiLibraryService(string rootDir)
    {
        _rootDir = rootDir;
        _libraryDir = Path.Combine(_rootDir, "library");
        _manifestPath = Path.Combine(_rootDir, "library.json");
    }

    private static string DefaultRootDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rimshot");

    public async Task LoadAsync()
    {
        Directory.CreateDirectory(_libraryDir);

        if (!File.Exists(_manifestPath))
        {
            Entries.Clear();
            return;
        }

        List<MidiLibraryEntry>? loaded = null;
        try
        {
            using var stream = File.OpenRead(_manifestPath);
            loaded = await JsonSerializer.DeserializeAsync<List<MidiLibraryEntry>>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MidiLibraryService: failed to read {_manifestPath}: {ex.Message}");
        }

        Entries.Clear();
        bool changed = false;
        foreach (var entry in loaded ?? new List<MidiLibraryEntry>())
        {
            if (File.Exists(StoredPath(entry)))
                Entries.Add(entry);
            else
                changed = true; // orphaned entry — drop and persist
        }

        if (changed) await SaveAsync();
    }

    public async Task<MidiLibraryEntry> ImportAsync(string sourcePath)
    {
        Directory.CreateDirectory(_libraryDir);

        var id = Guid.NewGuid();
        var storedFileName = id.ToString("N") + Path.GetExtension(sourcePath);
        var destPath = Path.Combine(_libraryDir, storedFileName);

        await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: false));

        double? bpm = null;
        double? lengthEighths = null;
        try
        {
            var (parsed, originalBpm) = await Task.Run(() => SongLoader.LoadWithBpm(destPath));
            bpm = originalBpm;
            lengthEighths = parsed.TotalEighths;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MidiLibraryService: metadata parse failed for {sourcePath}: {ex.Message}");
        }

        var entry = new MidiLibraryEntry(
            Id: id,
            DisplayName: Path.GetFileNameWithoutExtension(sourcePath),
            OriginalPath: sourcePath,
            StoredFileName: storedFileName,
            AddedAt: DateTime.UtcNow,
            LastPlayedAt: null,
            DetectedBpm: bpm,
            LengthEighths: lengthEighths
        );

        Entries.Add(entry);
        await SaveAsync();
        return entry;
    }

    public async Task<Song> LoadSongAsync(MidiLibraryEntry entry)
    {
        var path = StoredPath(entry);
        var parsed = await Task.Run(() => SongLoader.Load(path));
        // SongLoader names the song from the file path, but the stored file
        // is named <guid>.mid — substitute the user-facing display name.
        var song = parsed with { Name = entry.DisplayName };
        TouchLastPlayed(entry);
        await SaveAsync();
        return song;
    }

    public async Task RemoveAsync(Guid id)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return;

        var path = StoredPath(entry);
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MidiLibraryService: failed to delete {path}: {ex.Message}");
        }

        Entries.Remove(entry);
        await SaveAsync();
    }

    public MidiLibraryEntry? FindByOriginalPath(string sourcePath) =>
        Entries.FirstOrDefault(e =>
            string.Equals(e.OriginalPath, sourcePath, StringComparison.OrdinalIgnoreCase));

    private string StoredPath(MidiLibraryEntry entry) => Path.Combine(_libraryDir, entry.StoredFileName);

    private void TouchLastPlayed(MidiLibraryEntry entry)
    {
        var idx = Entries.IndexOf(entry);
        if (idx < 0) return;
        Entries[idx] = entry with { LastPlayedAt = DateTime.UtcNow };
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(_rootDir);
        var tmp = _manifestPath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, Entries.ToList(), JsonOptions);
        }
        File.Move(tmp, _manifestPath, overwrite: true);
    }
}
