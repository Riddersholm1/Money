using System.Text;

namespace Riddersholm.Money.Generators;

/// <summary>
/// Culture-invariant <see cref="StringBuilder"/> helpers.
/// </summary>
/// <remarks>
/// netstandard2.0 has no <c>AppendLine(IFormatProvider, ...)</c> overload, and generated source must
/// never depend on the compiler machine's culture — a Turkish or Arabic build agent producing different
/// output than a US one is exactly the kind of bug that only shows up in someone else's CI.
/// </remarks>
internal static class SourceBuilderExtensions
{
    extension(StringBuilder builder)
    {
        public StringBuilder Line(FormattableString text) =>
            builder.AppendLine(FormattableString.Invariant(text));

        public StringBuilder Line(string text) =>
            builder.AppendLine(text);

        public StringBuilder Line() =>
            builder.AppendLine();

        public StringBuilder Text(FormattableString text) =>
            builder.Append(FormattableString.Invariant(text));
    }
}
