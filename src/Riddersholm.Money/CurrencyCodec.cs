namespace Riddersholm.Money;

/// <summary>
/// Packs an ISO 4217 alphabetic code into a 32-bit integer and back.
/// </summary>
/// <remarks>
/// <para>
/// Each of the three letters occupies five bits holding <c>1..26</c>, so a code always round-trips
/// exactly and <c>0</c> is unreachable from any real code. That free slot is given to <c>XXX</c> —
/// ISO's "no currency" — which is what makes <c>default(Currency)</c>, <see cref="Currency.None"/>,
/// and <c>Currency.XXX</c> a single value rather than three near-synonyms.
/// </para>
/// <para>
/// Because the code <em>is</em> the value, a currency loaded from a database round-trips byte for byte
/// even when the library has never heard of it. A design that stored an index into a lookup table
/// could not: an unrecognised code would have to throw or collapse to a sentinel, losing data that was
/// perfectly good on the way in.
/// </para>
/// <para>
/// <c>Riddersholm.Money.Generators.CurrencyCodec</c> holds a deliberate copy of the packing half of
/// this algorithm, because a Roslyn component cannot reference this assembly. <c>CurrencyCodecTests</c>
/// asserts that every generated constant equals <see cref="Currency.FromCode(string)"/> at runtime, so
/// the two cannot drift apart unnoticed.
/// </para>
/// </remarks>
internal static class CurrencyCodec
{
    /// <summary>The reserved packed value shared by <c>XXX</c>, <c>None</c>, and <c>default</c>.</summary>
    public const uint None = 0u;

    /// <summary>The value <c>XXX</c> would occupy without the reservation above.</summary>
    private const uint NaturalXxx = 24u | (24u << 5) | (24u << 10);

    private const string NoCurrencyCode = "XXX";

    /// <summary>
    /// Packs three ASCII letters, returning <see langword="false"/> for anything else. Lower case is
    /// accepted and normalised to upper: input is forgiving, output is always canonical.
    /// </summary>
    public static bool TryPack(ReadOnlySpan<char> code, out uint packed)
    {
        packed = None;

        if (code.Length != 3)
        {
            return false;
        }

        uint result = 0;
        for (int i = 0; i < 3; i++)
        {
            char c = code[i];
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }

            result |= (uint)((c | 0x20) - 'a' + 1) << (i * 5);
        }

        packed = result == NaturalXxx ? None : result;
        return true;
    }

    /// <summary>Packs a UTF-8 code without transcoding it to UTF-16 first.</summary>
    public static bool TryPackUtf8(ReadOnlySpan<byte> code, out uint packed)
    {
        packed = None;

        if (code.Length != 3)
        {
            return false;
        }

        uint result = 0;
        for (int i = 0; i < 3; i++)
        {
            byte b = code[i];
            if (!char.IsAsciiLetter((char)b))
            {
                return false;
            }

            result |= (uint)((b | 0x20) - 'a' + 1) << (i * 5);
        }

        packed = result == NaturalXxx ? None : result;
        return true;
    }

    /// <summary>Writes the three-letter code into <paramref name="destination"/>, which must hold at least 3 chars.</summary>
    public static void Unpack(uint packed, Span<char> destination)
    {
        if (packed == None)
        {
            NoCurrencyCode.CopyTo(destination);
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            destination[i] = (char)('A' + (int)((packed >> (i * 5)) & 0x1F) - 1);
        }
    }

    /// <summary>Writes the three-letter code as UTF-8, which for ASCII is the same bytes.</summary>
    public static void UnpackUtf8(uint packed, Span<byte> destination)
    {
        if (packed == None)
        {
            "XXX"u8.CopyTo(destination);
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            destination[i] = (byte)('A' + (int)((packed >> (i * 5)) & 0x1F) - 1);
        }
    }

    /// <summary>
    /// Materialises the code as a string. Callers that already know the currency should prefer the
    /// generated literal via <see cref="Currency.Code"/>; this allocates and exists for codes the
    /// library has never seen.
    /// </summary>
    public static string Decode(uint packed)
    {
        if (packed == None)
        {
            return NoCurrencyCode;
        }

        return string.Create(3, packed, static (destination, value) => Unpack(value, destination));
    }
}
