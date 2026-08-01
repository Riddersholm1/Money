using System.Globalization;
using System.Text;
using System.Text.Json;
using Riddersholm.Money;
using Riddersholm.Money.Serialization;

// Exercises every part of the public surface that could plausibly need reflection, then asserts the
// results. CI publishes this with PublishAot=true and TrimMode=full and runs the native binary, so a
// regression shows up either as a build failure (an IL2xxx/IL3xxx warning, which is an error here) or
// as a non-zero exit code.

int failures = 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        Console.WriteLine($"  ok    {description}");
        return;
    }

    Console.WriteLine($"  FAIL  {description}");
    failures++;
}

Console.WriteLine("Riddersholm.Money — NativeAOT smoke test");
Console.WriteLine();

Console.WriteLine("currency table (generated, no reflection)");
Check(Currency.Known.Length == 166, $"166 currencies generated (found {Currency.Known.Length})");
Check(Currency.DKK.Code == "DKK", "generated constants resolve");
Check(Currency.DKK.EnglishName == "Danish Krone", "metadata is reachable after trimming");
Check(Currency.DKK.Symbol == "kr", "symbols survive trimming");
Check(Currency.JPY.DecimalDigits == 0, "per-currency precision survives trimming");
Check(default(Currency) == Currency.XXX, "default currency is XXX");
Check(Currency.FromCode("QQQ").Code == "QQQ", "unknown codes round-trip");

Console.WriteLine();
Console.WriteLine("arithmetic");
Money price = new(100m, Currency.DKK);
Check((price + new Money(50m, Currency.DKK)).Amount == 150m, "addition");
Check((price * 2.5m).Amount == 250m, "multiplication");
Check((price / 4m).Amount == 25m, "division");
Check((-price).Amount == -100m, "negation");
Check(new Money(2.225m, Currency.DKK).Round().Amount == 2.22m, "banker's rounding");
Check(new Money(1.37m, Currency.MRU).Round().Amount == 1.4m, "fifth minor units round to the increment");
Check(new Money(12.30m, Currency.DKK).RoundToCash().Amount == 12.50m, "cash rounding");

Console.WriteLine();
Console.WriteLine("allocation");
Money[] parts = new Money(10m, Currency.DKK).Allocate(3);
Check(parts.Length == 3 && parts[0].Amount == 3.34m && parts[1].Amount == 3.33m, "equal allocation");
Check(parts.Sum() == new Money(10m, Currency.DKK), "allocation preserves the total");

Money[] ratios = new Money(100m, Currency.DKK).Allocate([70, 30]);
Check(ratios[0].Amount == 70m && ratios[1].Amount == 30m, "ratio allocation");

Console.WriteLine();
Console.WriteLine("formatting (globalization data must survive trimming)");
CultureInfo danish = new("da-DK");
Check(price.ToString("G", CultureInfo.InvariantCulture) == "100.00 DKK", "general format");
Check(price.ToString("I", CultureInfo.InvariantCulture) == "DKK 100.00", "ISO format");
Check(new Money(100.5m, Currency.DKK).ToString("C", danish) == "100,50 kr.", "localised currency format");
Check(new Money(1234m, Currency.JPY).ToString("C", new CultureInfo("en-US")) == "¥1,234", "currency precision, not culture precision");

Span<char> buffer = stackalloc char[64];
Check(price.TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture)
      && buffer[..written].SequenceEqual("100.00 DKK"), "span formatting");

Span<byte> utf8 = stackalloc byte[64];
Check(price.TryFormat(utf8, out int utf8Written, "R", CultureInfo.InvariantCulture)
      && utf8[..utf8Written].SequenceEqual("100.00 DKK"u8), "UTF-8 formatting");

Console.WriteLine();
Console.WriteLine("parsing");
Check(Money.Parse("100.50 DKK", CultureInfo.InvariantCulture).Amount == 100.50m, "ISO code parsing");
Check(Money.Parse("100,50 kr.", danish) == new Money(100.50m, Currency.DKK), "symbol parsing with a culture");
Check(!Money.TryParse("100 kr.", CultureInfo.InvariantCulture, out _), "ambiguous symbols are refused");
Check(Money.Parse("100.50 DKK"u8, CultureInfo.InvariantCulture).Amount == 100.50m, "UTF-8 parsing");

Console.WriteLine();
Console.WriteLine("json (source-generated context, no reflection)");
// JSON numbers carry no scale, so the amount is written as the decimal's own value rather than
// padded to the currency's precision the way display formatting is.
Money withDecimals = new(100.50m, Currency.DKK);
string json = JsonSerializer.Serialize(withDecimals, MoneyJsonSerializerContext.Default.Money);
Check(json == """{"amount":100.50,"currency":"DKK"}""", $"serialisation ({json})");
Check(JsonSerializer.Deserialize(json, MoneyJsonSerializerContext.Default.Money) == withDecimals, "deserialisation");

byte[] utf8Json = Encoding.UTF8.GetBytes("""{"amount":42.5,"currency":"EUR"}""");
Money fromUtf8 = JsonSerializer.Deserialize(utf8Json, MoneyJsonSerializerContext.Default.Money);
Check(fromUtf8 == new Money(42.5m, Currency.EUR), "UTF-8 deserialisation");

Console.WriteLine();
Console.WriteLine("runtime-registered currencies (module initializer path)");
CurrencyRegistry.Register(new CurrencyInfo("XBT", 0, "Bitcoin", "₿", 8, 100_000_000L, 8, 1));
Currency bitcoin = Currency.FromCode("XBT");
Check(bitcoin.IsKnown && bitcoin.EnglishName == "Bitcoin", "registration works without reflection");
Check(bitcoin.DecimalDigits == 8, "registered precision beyond the ISO maximum");

Console.WriteLine();
Console.WriteLine("exchange rate");
ExchangeRate rate = new(Currency.DKK, Currency.EUR, 0.134m);
Check(rate.Convert(price).Round().Amount == 13.40m, "conversion");

Console.WriteLine();

if (failures == 0)
{
    Console.WriteLine("All NativeAOT checks passed.");
    return 0;
}

Console.WriteLine($"{failures} NativeAOT check(s) failed.");
return 1;
