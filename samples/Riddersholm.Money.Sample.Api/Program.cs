using System.Globalization;
using Riddersholm.Money;

// Shows that Money needs no special plumbing in a minimal API: it binds from route and query
// parameters through IParsable, and from request bodies through its JSON converter.
//
//   dotnet run --project samples/Riddersholm.Money.Sample.Api
//   curl localhost:5000/quote/100.50%20DKK
//   curl -X POST localhost:5000/orders -H 'content-type: application/json' \
//        -d '{"reference":"A-1","lines":[{"description":"Widget","unitPrice":{"amount":19.99,"currency":"DKK"},"quantity":3}]}'

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

// Money implements IParsable, so ASP.NET Core binds it from a route parameter with no configuration.
// Try "100.50 DKK" or "DKK 100.50"; "100 kr." is refused, because a symbol needs a culture.
app.MapGet("/quote/{price}", (Money price) => new
{
    price,
    rounded = price.Round(),
    isCanonical = price.IsCanonical,
    formatted = new
    {
        general = price.ToString("G", CultureInfo.InvariantCulture),
        iso = price.ToString("I", CultureInfo.InvariantCulture),
        danish = price.ToString("C", new CultureInfo("da-DK")),
    },
});

// Currency binds the same way.
app.MapGet("/currencies/{currency}", (Currency currency) =>
    currency.IsKnown
        ? Results.Ok(new
        {
            code = currency.Code,
            numericCode = currency.NumericCode,
            name = currency.EnglishName,
            symbol = currency.Symbol,
            decimalDigits = currency.DecimalDigits,
            minorUnitsPerMajor = currency.MinorUnitsPerMajor,
            smallestAmount = currency.MinorUnit,
        })
        : Results.NotFound(new { code = currency.Code, message = "Not a currency this build knows about." }));

app.MapGet("/currencies", () => Currency.Known.ToArray().Select(c => new { code = c.Code, name = c.EnglishName }));

// Money round-trips through the request and response body with no converter registration.
app.MapPost("/orders", (Order order) =>
{
    Money subtotal = order.Lines.Sum(line => line.UnitPrice * line.Quantity);
    Money vat = (subtotal * 0.25m).Round();
    Money total = (subtotal + vat).Round();

    return Results.Ok(new
    {
        order.Reference,
        subtotal = subtotal.Round(),
        vat,
        total,
    });
});

// Splitting a total between payers, with the guarantee that the parts sum to the whole.
app.MapPost("/orders/split", (SplitRequest request) =>
{
    Money total = request.Total.Round();
    Money[] shares = request.Ratios is { Length: > 0 }
        ? total.Allocate(request.Ratios)
        : total.Allocate(request.Payers);

    return Results.Ok(new
    {
        total,
        shares,
        // Always exactly zero: that is the point of allocating rather than dividing.
        unallocated = total - shares.Sum(),
    });
});

app.Run();

internal sealed record Order(string Reference, OrderLine[] Lines);

internal sealed record OrderLine(string Description, Money UnitPrice, int Quantity);

internal sealed record SplitRequest(Money Total, int Payers, int[]? Ratios);
