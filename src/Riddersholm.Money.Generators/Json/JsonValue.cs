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

    /// <summary>An integer literal, kept exact rather than widened to a double.</summary>
    /// <remarks>
    /// A double stops representing every integer at 2^53, so a minor-unit divisor written as
    /// <c>999999999999999999</c> would otherwise arrive as <c>10^18</c> — a different currency.
    /// </remarks>
    public static JsonValue From(long value) => new(value);

    public static JsonValue From(bool value) => new(value);

    public static JsonValue From(Dictionary<string, JsonValue> value) => new(value);

    public static JsonValue From(List<JsonValue> value) => new(value);

    public string? AsString() => _value as string;

    public IReadOnlyList<JsonValue> AsArray() => _value as List<JsonValue> ?? [];

    /// <summary>Reads a property, returning <see cref="Null"/> when this is not an object or the key is absent.</summary>
    public JsonValue this[string key] =>
        _value is Dictionary<string, JsonValue> map && map.TryGetValue(key, out JsonValue? value) ? value : Null;

    /// <summary>Reads an integer, refusing anything that is not exactly one.</summary>
    /// <remarks>
    /// Strict on purpose. The lenient version this replaced truncated <c>2.7</c> to <c>2</c> and turned
    /// <c>"two"</c> into a silent default, so a typo in a currency file became a currency with the wrong
    /// precision rather than a build error. An integer written as a quoted string is still accepted,
    /// because that is unambiguous.
    /// </remarks>
    public bool TryGetInt64(out long value)
    {
        switch (_value)
        {
            case long integer:
                value = integer;
                return true;
            case double d when d == Math.Floor(d) && d is >= -9_007_199_254_740_992d and <= 9_007_199_254_740_992d:
                // An integral double within 2^53 is exact; "2.0" and "2e1" are integers in JSON's eyes.
                value = (long)d;
                return true;
            case string s when long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public bool IsNull => _value is null;
}
