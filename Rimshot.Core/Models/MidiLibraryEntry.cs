using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rimshot.Core.Models;

public record MidiLibraryEntry(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    string StoredFileName,
    DateTime AddedAt,
    DateTime? LastPlayedAt = null,
    double? DetectedBpm = null,
    double? LengthEighths = null
)
{
    [JsonIgnore]
    public string MetadataLine
    {
        get
        {
            var parts = new List<string>();
            if (DetectedBpm.HasValue) parts.Add($"{Math.Round(DetectedBpm.Value)} BPM");
            if (LengthEighths.HasValue) parts.Add($"{LengthEighths.Value / 8.0:F0} bars");
            parts.Add(LastPlayedAt.HasValue
                ? $"last played {LastPlayedAt.Value.ToLocalTime():MMM d}"
                : $"added {AddedAt.ToLocalTime():MMM d}");
            return string.Join("  ·  ", parts);
        }
    }
}
