using BenchmarkDotNet.Running;

// Run everything:      dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks
// Run one suite:       dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks -- --filter *Formatting*
// Quick smoke run:     dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks -- --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Entry point marker for <see cref="BenchmarkSwitcher"/>.</summary>
internal sealed partial class Program;
