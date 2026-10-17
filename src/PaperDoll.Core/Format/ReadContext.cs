// JSON value reading for INP1, as lenient as nijilive's reader: numbers written as integers or floats, "nan"/"inf"
// strings for non-finite numbers, names cut at the first NUL, enum names in either spelling. A value that cannot be read
// falls back to its default, and every unknown key, odd value and fallback is recorded for the load report.

using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace PaperDoll.Format;

/// <summary>The keys an object kind uses, and the keys known to be editor data that are ignored.</summary>
internal sealed class KeySet(string kind, string[] used, string[]? ignored = null)
{
    public string Kind { get; } = kind;
    public HashSet<string> Used { get; } = new(used, StringComparer.Ordinal);
    public HashSet<string> Ignored { get; } = new(ignored ?? [], StringComparer.Ordinal);

    public KeySet Extend(string kind, string[] used, string[]? ignored = null) =>
        new(kind, [.. Used, .. used], [.. Ignored, .. ignored ?? []]);
}

/// <summary>Collects load issues (merging repeats) and reads JSON values with defaults.</summary>
internal sealed class ReadContext
{
    private readonly List<(LoadIssueKind Kind, string Location, string Message)> _order = [];
    private readonly Dictionary<(LoadIssueKind, string, string), int> _counts = [];

    public void Report(LoadIssueKind kind, string location, string message)
    {
        var key = (kind, location, message);
        if (_counts.TryGetValue(key, out var count))
        {
            _counts[key] = count + 1;
            return;
        }

        _counts[key] = 1;
        _order.Add(key);
    }

    public List<LoadIssue> BuildIssues() =>
        _order.ConvertAll(i => new LoadIssue(i.Kind, i.Location, i.Message, _counts[i]));

    public void CheckKeys(JsonElement obj, KeySet keys)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (keys.Used.Contains(property.Name))
                continue;
            if (keys.Ignored.Contains(property.Name))
                Report(LoadIssueKind.IgnoredKey, keys.Kind, $"editor key '{property.Name}' ignored");
            else
                Report(LoadIssueKind.UnknownKey, keys.Kind, $"unknown key '{property.Name}' ignored");
        }
    }

    /// <summary>Gets a property that is present and not <c>null</c>.</summary>
    public static bool TryGet(JsonElement obj, string key, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out value) && value.ValueKind != JsonValueKind.Null)
            return true;
        value = default;
        return false;
    }

    public float Float(JsonElement obj, string key, float fallback, string location) =>
        TryGet(obj, key, out var value) ? Float(value, fallback, $"{location}.{key}") : fallback;

    public float Float(JsonElement value, float fallback, string location)
    {
        if (TryNumber(value, out var number))
            return (float)number;
        Report(LoadIssueKind.InvalidValue, location, $"expected a number, found {Describe(value)}; using {fallback.ToString(CultureInfo.InvariantCulture)}");
        return fallback;
    }

    /// <summary>Reads a number written as a JSON number or as a "nan"/"inf" string.</summary>
    public static bool TryNumber(JsonElement value, out double number)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDouble(out number);
            case JsonValueKind.String:
                var text = (value.GetString() ?? "").Trim().Trim('"').ToLowerInvariant();
                switch (text)
                {
                    case "nan" or "+nan" or "-nan":
                        number = double.NaN;
                        return true;
                    case "inf" or "+inf" or "infinity" or "+infinity":
                        number = double.PositiveInfinity;
                        return true;
                    case "-inf" or "-infinity":
                        number = double.NegativeInfinity;
                        return true;
                    default:
                        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                }
            default:
                number = 0;
                return false;
        }
    }

    public int Int(JsonElement obj, string key, int fallback, string location)
    {
        if (!TryGet(obj, key, out var value))
            return fallback;
        if (TryNumber(value, out var number) && double.IsFinite(number) && number >= int.MinValue && number <= int.MaxValue)
            return (int)number;
        Report(LoadIssueKind.InvalidValue, $"{location}.{key}", $"expected an integer, found {Describe(value)}; using {fallback}");
        return fallback;
    }

    public bool Bool(JsonElement obj, string key, bool fallback, string location)
    {
        if (!TryGet(obj, key, out var value))
            return fallback;
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number when value.TryGetDouble(out var number):
                return number != 0;
            default:
                Report(LoadIssueKind.InvalidValue, $"{location}.{key}", $"expected true or false, found {Describe(value)}; using {fallback}");
                return fallback;
        }
    }

    /// <summary>Reads an unsigned 32-bit ID. <c>4294967295</c> (or -1) means none and gives <see langword="null"/>.</summary>
    public uint? Id(JsonElement obj, string key, string location)
    {
        if (!TryGet(obj, key, out var value))
            return null;
        return Id(value, $"{location}.{key}");
    }

    public uint? Id(JsonElement value, string location)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetUInt32(out var id))
                return id == uint.MaxValue ? null : id;
            if (value.TryGetInt64(out var signed) && signed == -1)
                return null;
        }

        Report(LoadIssueKind.InvalidValue, location, $"expected an unsigned 32-bit ID, found {Describe(value)}; treated as none");
        return null;
    }

    /// <summary>Reads a string, dropping trailing NUL padding. <c>null</c> stays <see langword="null"/>.</summary>
    public string? String(JsonElement obj, string key, string location)
    {
        if (!TryGet(obj, key, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()!.TrimEnd('\0');
        Report(LoadIssueKind.InvalidValue, $"{location}.{key}", $"expected a string, found {Describe(value)}");
        return null;
    }

    /// <summary>Reads a name, cut at the first NUL.</summary>
    public string Name(JsonElement obj, string key, string location)
    {
        var text = String(obj, key, location) ?? "";
        var nul = text.IndexOf('\0', StringComparison.Ordinal);
        return nul >= 0 ? text[..nul] : text;
    }

    public Vector2 Vec2(JsonElement obj, string key, Vector2 fallback, string location)
    {
        if (!TryGet(obj, key, out var value))
            return fallback;
        Span<float> parts = [fallback.X, fallback.Y];
        ReadVector(value, parts, $"{location}.{key}");
        return new Vector2(parts[0], parts[1]);
    }

    public Vector3 Vec3(JsonElement obj, string key, Vector3 fallback, string location)
    {
        if (!TryGet(obj, key, out var value))
            return fallback;
        Span<float> parts = [fallback.X, fallback.Y, fallback.Z];
        ReadVector(value, parts, $"{location}.{key}");
        return new Vector3(parts[0], parts[1], parts[2]);
    }

    private void ReadVector(JsonElement value, Span<float> parts, string location)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < parts.Length)
        {
            Report(LoadIssueKind.InvalidValue, location, $"expected an array of {parts.Length} numbers, found {Describe(value)}; using the default");
            return;
        }

        var i = 0;
        foreach (var element in value.EnumerateArray())
        {
            if (i == parts.Length)
                break;
            parts[i] = Float(element, parts[i], location);
            i++;
        }
    }

    /// <summary>
    /// Reads an enum by member name. Case and underscores are ignored, so both the member name (<c>SpringPendulum</c>) and
    /// the format's string value (<c>spring_pendulum</c>) are accepted. A number is accepted if it names a member.
    /// </summary>
    public T Enum<T>(JsonElement obj, string key, T fallback, string location) where T : struct, System.Enum
    {
        if (!TryGet(obj, key, out var value))
            return fallback;
        if (TryEnum<T>(value, out var result))
            return result;
        Report(LoadIssueKind.InvalidValue, $"{location}.{key}", $"unknown {typeof(T).Name} {Describe(value)}; using {fallback}");
        return fallback;
    }

    public static bool TryEnum<T>(JsonElement value, out T result) where T : struct, System.Enum
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = (value.GetString() ?? "").Replace("_", "", StringComparison.Ordinal).Trim();
            if (text.Length > 0 && !char.IsAsciiDigit(text[0]) && text[0] != '-' &&
                System.Enum.TryParse(text, ignoreCase: true, out result) && System.Enum.IsDefined(result))
            {
                return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            foreach (var candidate in System.Enum.GetValues<T>())
            {
                if (Convert.ToInt32(candidate, CultureInfo.InvariantCulture) == number)
                {
                    result = candidate;
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    public static string Describe(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => $"an array of {value.GetArrayLength()}",
            JsonValueKind.String => $"\"{value.GetString()}\"",
            _ => value.GetRawText(),
        };
        return text.Length > 60 ? text[..57] + "..." : text;
    }
}
