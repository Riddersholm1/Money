using System.Globalization;

namespace Riddersholm.Money.Generators.Json;

/// <summary>
/// A minimal JSON document model.
/// </summary>
/// <remarks>
/// Source generators must not carry package dependencies: anything the generator references has to be
/// shipped alongside it in <c>analyzers/dotnet/cs</c>, and a version clash there breaks every consumer's
/// build. <c>System.Text.Json</c> is also unavailable to netstandard2.0 without such a reference, so the
/// few hundred lines needed to read a known-shape file are written by hand instead.
/// </remarks>
internal sealed class JsonValue
{
    private readonly object? _value;

    private JsonValue(object? value)
    {
        _value = value;
    }

    public static JsonValue Null { get; } = new(null);

    public static JsonValue From(string value) => new(value);

    public static JsonValue From(double value) => new(value);

    public static JsonValue From(bool value) => new(value);

    public static JsonValue From(Dictionary<string, JsonValue> value) => new(value);

    public static JsonValue From(List<JsonValue> value) => new(value);

    public string? AsString() => _value as string;

    public IReadOnlyList<JsonValue> AsArray() => _value as List<JsonValue> ?? [];

    /// <summary>Reads a property, returning <see cref="Null"/> when this is not an object or the key is absent.</summary>
    public JsonValue this[string key] =>
        _value is Dictionary<string, JsonValue> map && map.TryGetValue(key, out JsonValue? value) ? value : Null;

    /// <summary>Reads a numeric value, falling back when the property is missing or not a number.</summary>
    public long AsInt64(long fallback = 0) => _value switch
    {
        double d => (long)d,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
        _ => fallback
    };

    public bool IsNull => _value is null;
}
