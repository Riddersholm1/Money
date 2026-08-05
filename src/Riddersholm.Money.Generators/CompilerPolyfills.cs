// netstandard2.0 predates these compiler-only types. They exist purely so that records and
// 'required'-style analysis compile; nothing here is ever emitted into generated output.
//
// THE NAMESPACE IS LOAD-BEARING. The compiler resolves this type by its exact fully-qualified name,
// System.Runtime.CompilerServices.IsExternalInit, and by no other means. Moving it into the project's
// own namespace — which is what a "namespace must match folder structure" rule will suggest — makes
// every 'init' accessor and every record in this assembly fail with CS0518, as it did once already.
// Declaring a type in a System namespace is otherwise poor practice, which is why the suppression
// below is scoped to this one file instead of being switched off repository-wide.

// Two tools, two suppressions. The pragma silences Roslyn's IDE0130; ReSharper and Rider use their own
// inspection IDs and would otherwise keep offering "move to Riddersholm.Money.Generators" as a one-key
// quick-fix, with nothing to warn that taking it stops the assembly compiling.
#pragma warning disable IDE0130 // Namespace does not match folder structure — deliberate, see above.

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

// Referenced by the compiler, never by our code, so every "unused" analysis is right and irrelevant.
// ReSharper disable once UnusedType.Global
internal static class IsExternalInit;

#pragma warning restore IDE0130
