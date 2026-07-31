using System;
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
    public static StringBuilder Line(this StringBuilder builder, FormattableString text) =>
        builder.AppendLine(FormattableString.Invariant(text));

    public static StringBuilder Line(this StringBuilder builder, string text) =>
        builder.AppendLine(text);

    public static StringBuilder Line(this StringBuilder builder) =>
        builder.AppendLine();

    public static StringBuilder Text(this StringBuilder builder, FormattableString text) =>
        builder.Append(FormattableString.Invariant(text));
}
