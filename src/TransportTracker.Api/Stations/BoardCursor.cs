using System.Buffers.Text;
using System.Text;

namespace TransportTracker.Api.Stations;

/// <summary>
/// Where a page of a departure board starts, as an opaque string clients pass back unchanged.
/// Later pages continue the upcoming trains after a position; earlier pages go back through the
/// trains that have already left, before a position (or from the most recent, when Position is null).
/// A position is a departure's sort time plus its Trip ID, so trains leaving at the same second
/// are neither skipped nor repeated across pages.
/// </summary>
public record BoardCursor(bool Earlier, BoardPosition? Position)
{
    public string Encode()
    {
        var text = $"{(Earlier ? 'e' : 'l')}|{Position?.At.ToUnixTimeSeconds()}|{Position?.TripId}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }

    public static bool TryParse(string? value, out BoardCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value)) return true;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split('|', 3);
            if (parts.Length != 3 || parts[0] is not ("e" or "l")) return false;

            BoardPosition? position = null;
            if (parts[1] != "")
            {
                if (!long.TryParse(parts[1], out var seconds)) return false;
                position = new BoardPosition(DateTimeOffset.FromUnixTimeSeconds(seconds), parts[2]);
            }
            if (parts[0] == "l" && position is null) return false; // A later page always continues from somewhere.

            cursor = new BoardCursor(parts[0] == "e", position);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>A departure's place in board order: by time, then Trip ID.</summary>
public record BoardPosition(DateTimeOffset At, string TripId) : IComparable<BoardPosition>
{
    public int CompareTo(BoardPosition? other) =>
        other is null ? 1 : At != other.At ? At.CompareTo(other.At) : string.CompareOrdinal(TripId, other.TripId);
}
