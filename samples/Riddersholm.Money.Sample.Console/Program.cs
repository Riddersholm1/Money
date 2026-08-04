using System.Globalization;
using System.Text.Json;
using Riddersholm.Money;

// A guided tour. Run with:
//   dotnet run --project samples/Riddersholm.Money.Sample.Console

CultureInfo invariant = CultureInfo.InvariantCulture;
CultureInfo danish = new("da-DK");
CultureInfo american = new("en-US");

Section("Amounts are exact");

Money unitPrice = new(19.99m, Currency.DKK);
Money subtotal = unitPrice * 3;
Money withVat = subtotal * 1.25m;

Console.WriteLine($"  unit price          {unitPrice}");
Console.WriteLine($"  × 3                 {subtotal}");
Console.WriteLine($"  × 1.25 VAT          {withVat}          <- kept exactly, not rounded");
Console.WriteLine($"  IsCanonical         {withVat.IsCanonical}");
Console.WriteLine($"  .Round()            {withVat.Round()}          <- your decision, stated once");

Section("Mixing currencies is refused");

try
{
    _ = new Money(100m, Currency.DKK) + new Money(50m, Currency.EUR);
}
catch (CurrencyMismatchException error)
{
    Console.WriteLine($"  {error.Message}");
}

Console.WriteLine($"  but equality is fine: 100 DKK == 100 EUR is {new Money(100m, Currency.DKK) == new Money(100m, Currency.EUR)}");

Section("Splitting money never loses any");

Money bill = new(10m, Currency.DKK);
Money[] shares = bill.Allocate(3);

Console.WriteLine($"  {bill} across 3    {string.Join(", ", shares.Select(s => s.ToString("R", invariant)))}");
Console.WriteLine($"  they sum to        {shares.Sum()}          <- exactly the original");

Money[] split = new Money(100m, Currency.DKK).Allocate([70, 30]);
Console.WriteLine($"  100 DKK at 70:30    {string.Join(", ", split.Select(s => s.ToString("R", invariant)))}");

Section("Currencies are not all decimal-hundredths");

foreach (Currency currency in (Currency[])[Currency.DKK, Currency.JPY, Currency.KWD, Currency.MRU, Currency.XXX])
{
    Console.WriteLine($"  {currency.Code}  digits={currency.DecimalDigits}  minorUnitsPerMajor={currency.MinorUnitsPerMajor,-6} minorUnit={currency.MinorUnit}");
}

Console.WriteLine();
Console.WriteLine($"  1.37 MRU rounds to  {new Money(1.37m, Currency.MRU).Round()}          <- the khoum is a fifth, so amounts step by 0.2");

Section("Cash rounds differently from ledgers");

Console.WriteLine($"  12.34 CHF ledger    {new Money(12.34m, Currency.CHF).Round()}");
Console.WriteLine($"  12.34 CHF till      {new Money(12.34m, Currency.CHF).RoundToCash()}          <- 5 centime coin");
Console.WriteLine($"  12.30 DKK till      {new Money(12.30m, Currency.DKK).RoundToCash()}          <- 50 øre coin");

Section("Formatting knows the currency's precision");

Money yen = new(1234m, Currency.JPY);
Money dinars = new(1.234m, Currency.KWD);
Money kroner = new(1234.5m, Currency.DKK);

Console.WriteLine($"  decimal .ToString(\"C\")   {1234m.ToString("C", american)}        <- the BCL: always two decimals");
Console.WriteLine($"  {yen.ToString("R", invariant),-16} C     {yen.ToString("C", american)}           <- yen has none");
Console.WriteLine($"  {dinars.ToString("R", invariant),-16} C     {dinars.ToString("C", american)}       <- dinars have three");
Console.WriteLine();
Console.WriteLine($"  G   {kroner.ToString("G", invariant)}");
Console.WriteLine($"  R   {kroner.ToString("R", invariant)}");
Console.WriteLine($"  I   {kroner.ToString("I", invariant)}");
Console.WriteLine($"  C   {kroner.ToString("C", danish)}   (da-DK)");
Console.WriteLine($"  N   {kroner.ToString("N", american)}");
Console.WriteLine($"  L   {kroner.ToString("L", invariant)}");

Section("Parsing refuses to guess");

Console.WriteLine($"  \"100.50 DKK\" invariant   {Money.Parse("100.50 DKK", invariant)}");
Console.WriteLine($"  \"DKK 100.50\" invariant   {Money.Parse("DKK 100.50", invariant)}");
Console.WriteLine($"  \"100,50 kr.\"  da-DK      {Money.Parse("100,50 kr.", danish)}");
Console.WriteLine($"  \"kr. 100\"     da-DK      {Money.Parse("kr. 100", danish)}");
Console.WriteLine($"  \"100 kr.\"     invariant  {(Money.TryParse("100 kr.", invariant, out Money ambiguous) ? ambiguous.ToString() : "refused — kr is DKK, NOK, SEK and ISK")}");

Section("Sorting works across currencies; comparing does not");

List<Money> mixed =
[
    new(50m, Currency.EUR),
    new(100m, Currency.DKK),
    new(10m, Currency.EUR)
];

mixed.Sort();
Console.WriteLine($"  sorted   {string.Join(", ", mixed.Select(m => m.ToString("R", invariant)))}");

try
{
    _ = new Money(100m, Currency.DKK) > new Money(50m, Currency.EUR);
}
catch (CurrencyMismatchException)
{
    Console.WriteLine("  but `dkk > eur` throws — that comparison is a bug in your code");
}

Section("JSON works with no configuration");

Money price = new(100.50m, Currency.DKK);
string json = JsonSerializer.Serialize(price);

Console.WriteLine($"  serialised    {json}");
Console.WriteLine($"  round-tripped {JsonSerializer.Deserialize<Money>(json)}");

Section("Converting currencies is possible, but never accidental");

ExchangeRate rate = new(Currency.DKK, Currency.EUR, 0.134m);

Console.WriteLine($"  rate          {rate}");
Console.WriteLine($"  100 DKK  ->   {rate.Convert(new Money(100m, Currency.DKK)).Round()}");

Section("Unknown currencies survive intact");

var future = Currency.FromCode("QQQ");

Console.WriteLine($"  code          {future.Code}");
Console.WriteLine($"  IsKnown       {future.IsKnown}");
Console.WriteLine($"  round-trips   {Money.Parse("42.00 QQQ", invariant)}");

try
{
    _ = new Money(42m, future).Round();
}
catch (UnknownCurrencyException error)
{
    Console.WriteLine($"  Round()       refused: {error.Message}");
}

Console.WriteLine($"  Round(2)      {new Money(42.005m, future).Round(2)}          <- explicit precision is fine");

Console.WriteLine();
Console.WriteLine($"{Currency.Known.Length} currencies available.");
return;

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('─', title.Length));
}
