using System.Globalization;
using System.Text;

namespace Riddersholm.Money.Generators;

/// <summary>
/// Turns currency data into text that is safe inside generated C#, shared by both emitters.
/// </summary>
/// <remarks>
/// <para>
/// Currency names and symbols come from a data file, and a data file can contain anything JSON can
/// escape. The emitters used to handle only backslashes and quotes, so a name carrying a line break —
/// one <c>\n</c> in the JSON — ended the string literal or the <c>///</c> comment it was written into,
/// and the rest of the name was compiled as code. Nothing hostile is needed for that: a copy-paste from
/// a spreadsheet will do it.
/// </para>
/// <para>
/// The two contexts need opposite treatment. A string literal must carry the exact text, because it is
/// what <c>Currency.EnglishName</c> returns at runtime. A documentation comment only has to read well,
/// so control characters there become spaces.
/// </para>
/// </remarks>
internal static class SourceEscaping
{
    /// <summary>A C# string literal, quotes included, that evaluates to exactly <paramref name="value"/>.</summary>
    public static string Literal(string value)
    {
        StringBuilder escaped = new(value.Length + 2);
        escaped.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                default:
                    // Covers \r, \n and the other C0/C1 controls, plus the two Unicode line separators,
                    // which C# also treats as the end of a line inside a literal.
                    if (char.IsControl(c) || c is '\u2028' or '\u2029')
                    {
                        escaped.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        escaped.Append(c);
                    }

                    break;
            }
        }

        return escaped.Append('"').ToString();
    }

    /// <summary>
    /// Text safe inside a single-line XML documentation comment.
    /// </summary>
    /// <remarks>
    /// Two ISO currencies carry a bare ampersand — "São Tomé &amp; Príncipe Dobra" and "Trinidad &amp;
    /// Tobago Dollar" — which is not valid XML and breaks the documentation build.
    /// </remarks>
    public static string XmlDoc(string value)
    {
        StringBuilder escaped = new(value.Length);

        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                default:
                    escaped.Append(char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c);
                    break;
            }
        }

        return escaped.ToString();
    }

    /// <summary>
    /// The <c>[Obsolete]</c> attribute for a withdrawn currency, or <see langword="null"/> for an active one.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: code that still names the member for historical data keeps
    /// compiling, while new code is told the currency is gone and, where ISO says so, what replaced it.
    /// </remarks>
    public static string? ObsoleteAttribute(CurrencyDefinition currency)
    {
        if (currency.Withdrawn is null)
        {
            return null;
        }

        string replacement = currency.ReplacedBy is null
            ? string.Empty
            : " and replaced by " + currency.ReplacedBy;

        string message =
            currency.Code + " was withdrawn from ISO 4217 in " + currency.Withdrawn + replacement + ". "
          + "It is kept so that historical amounts still have their precision; use Currency.FromCode(\""
          + currency.Code + "\") where stored data is read.";

        return "[global::System.Obsolete(" + Literal(message) + ")]";
    }

    /// <summary>The note appended to a withdrawn currency's documentation summary.</summary>
    public static string WithdrawnNote(CurrencyDefinition currency) =>
        currency.Withdrawn is null
            ? string.Empty
            : " Withdrawn from ISO 4217 in " + XmlDoc(currency.Withdrawn)
              + (currency.ReplacedBy is null ? "." : "; replaced by " + currency.ReplacedBy + ".");
}
