using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riddersholm.Money.Serialization;

/// <summary>Configures how <see cref="Money"/> is serialised.</summary>
public static class MoneyJsonExtensions
{
    /// <summary>Registers money converters, overriding the types' default shape.</summary>
    /// <param name="options">The options to configure.</param>
    /// <param name="format">How amounts should be written.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    /// <remarks>
    /// Only needed to change the write format: the default object shape works with no registration at
    /// all, because <see cref="Money"/> and <see cref="Currency"/> carry converter attributes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static JsonSerializerOptions AddMoney(this JsonSerializerOptions options, MoneyJsonFormat format = MoneyJsonFormat.NumericAmount)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new MoneyJsonConverter(format));
        options.Converters.Add(new CurrencyJsonConverter());

        return options;
    }
}

/// <summary>
/// A source-generated serialisation context for <see cref="Money"/> and <see cref="Currency"/>.
/// </summary>
/// <remarks>
/// Lets the types be serialised directly under NativeAOT and full trimming without reflection. For
/// your own DTOs, declare a context of your own — <see cref="Money"/> members will resolve through
/// their converter attributes with no extra configuration.
/// </remarks>
[JsonSerializable(typeof(Money))]
[JsonSerializable(typeof(Currency))]
[JsonSerializable(typeof(Money[]))]
public sealed partial class MoneyJsonSerializerContext : JsonSerializerContext;
