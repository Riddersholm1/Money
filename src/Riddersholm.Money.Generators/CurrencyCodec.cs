namespace Riddersholm.Money.Generators;

/// <summary>
/// Packs an ISO 4217 alphabetic code into an unsigned integer.
/// </summary>
/// <remarks>
/// <para>
/// This must stay byte-for-byte identical to <c>Riddersholm.Money.CurrencyCodec</c> in the runtime
/// library. The generator cannot reference the runtime library (it targets netstandard2.0 and runs
/// inside the compiler), so the algorithm is duplicated deliberately. A test in
/// <c>Riddersholm.Money.Tests</c> asserts that every generated constant equals
/// <c>Currency.FromCode(code)</c> at runtime, which fails loudly if the two copies ever drift.
/// </para>
/// <para>
/// Each letter occupies five bits holding <c>1..26</c>, so <c>0</c> is not reachable from any real
/// code. That free slot is given to <c>XXX</c> — ISO's "no currency" — which makes
/// <c>default(Currency)</c>, <c>Currency.None</c>, and <c>Currency.XXX</c> the same value.
/// </para>
/// </remarks>
internal static class CurrencyCodec
{
    /// <summary>The value <c>XXX</c> would occupy without the reservation below.</summary>
    private const uint NaturalXxx = 24u | (24u << 5) | (24u << 10);

    /// <summary>The reserved packed value shared by <c>XXX</c>, <c>None</c>, and <c>default</c>.</summary>
    public const uint None = 0u;

    public static bool TryPack(string code, out uint packed)
    {
        packed = 0;

        if (code is not { Length: 3 })
        {
            return false;
        }

        uint result = 0;
        for (int i = 0; i < 3; i++)
        {
            char c = code[i];
            if (c is < 'A' or > 'Z')
            {
                return false;
            }

            result |= (uint)(c - 'A' + 1) << (i * 5);
        }

        packed = result == NaturalXxx ? None : result;
        return true;
    }
}
