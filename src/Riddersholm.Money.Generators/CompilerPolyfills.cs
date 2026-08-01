// netstandard2.0 predates these compiler-only types. They exist purely so that records and
// 'required'-style analysis compile; nothing here is ever emitted into generated output.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
