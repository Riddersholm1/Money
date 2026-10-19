using System.Text;

namespace Riddersholm.Money.Generators;

/// <summary>
/// <see cref="StringBuilder"/> helpers for emitting generated source.
/// </summary>
/// <remarks>
/// <para>
/// Generated source must never depend on the compiler machine's culture — a Turkish or Arabic build
/// agent producing different output than a US one is exactly the kind of bug that only shows up in
/// someone else's CI. netstandard2.0 has no <c>AppendLine(IFormatProvider, ...)</c> overload, so the
/// interpolated members below route through <see cref="FormattableString.Invariant"/> instead.
/// </para>
/// <para>
/// <b>The invariant members are named rather than overloaded, and that is the whole point.</b> This
/// class previously offered <c>Line(FormattableString)</c> and <c>Line(string)</c> under one name. An
/// interpolated string literal converts better to <see cref="string"/> than to
/// <see cref="FormattableString"/>, so <em>every</em> interpolated call site silently bound to the
/// plain overload and formatted under the current culture: the invariant path was unreachable and the
/// protection described above did not exist. Nothing had failed yet, because the only interpolated
/// values are non-negative integers and .NET formats those with ASCII digits under every culture — but
/// the first emitted <see cref="decimal"/> would have written <c>-1234,5</c> on a Danish agent, and
/// that is not valid C#. Distinct names mean the compiler cannot choose the wrong one and a reader can
/// see which behaviour each call site asked for.
/// </para>
/// </remarks>
internal static class SourceBuilderExtensions
{
    extension(StringBuilder builder)
    {
        /// <summary>Appends a line of constant text. Nothing is formatted, so no culture applies.</summary>
        public StringBuilder Line(string text) =>
            builder.AppendLine(text);

        /// <summary>Appends a blank line.</summary>
        public StringBuilder Line() =>
            builder.AppendLine();

        /// <summary>Appends an interpolated line, formatting every value with the invariant culture.</summary>
        public StringBuilder LineInvariant(FormattableString text) =>
            builder.AppendLine(FormattableString.Invariant(text));

        /// <summary>Appends interpolated text without a line break, formatted invariantly.</summary>
        public StringBuilder TextInvariant(FormattableString text) =>
            builder.Append(FormattableString.Invariant(text));
    }
}
