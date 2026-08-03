# Swedish tax calculations for 2026

This ASP.NET Core Blazor Web App implements Skatteverket monthly tax tables 29
through 42 and the annual preliminary-tax formulas from SKV 433, edition 36,
for income year 2026.

The calculations use the same assumptions as the published tables and are not
an individualized final tax assessment.

## Web application

The application supports annual plans made from multiple income rows:

- annual or date-prorated monthly salary,
- one-time salary and termination payments,
- annual or date-prorated occupational pension,
- own-company dividends within the entered gränsbelopp,
- main-payer, secondary-payer, jämkning, actual withholding, and voluntary extra withholding in SEK per payment,
- vacation compensation, occupational-pension estimates, and salary exchange,
- mixed salary/pension annual tax, calculated withholding, and reconciliation,
- 2026 PGI and estimated SGI ceiling progress.

It also retains all six low-level tax-table columns and the complete annual
calculation from SKV 433 edition 36.

The calculator runs as interactive WebAssembly in the browser. Entered income
therefore stays in the browser and is not sent to the ASP.NET Core server.

## Projects

- The SwedishTax.Core project contains all tax, withholding, income-plan,
  pension, PGI/SGI, and projection logic. TaxProjection.Calculate is the
  UI-independent entry point for a complete plan.
- `src/SwedishTax.Web/SwedishTax.Web` hosts the Blazor application.
- `src/SwedishTax.Web/SwedishTax.Web.Client` contains the interactive client UI.
- The SwedishTax.Core.Tests project verifies every table boundary, the
  published formula examples, and complete planning/projection scenarios
  without loading the GUI.

The official fixed-width monthly table is embedded in `SwedishTax.Core`, so the
application does not need a database or an external service at runtime.

## Build and run

Install the .NET 10 SDK, then run:

```sh
dotnet restore SwedishTax.slnx
dotnet test SwedishTax.slnx -c Release
dotnet run --project src/SwedishTax.Web/SwedishTax.Web/SwedishTax.Web.csproj
```

The application prints its local URL when it starts.

## Sources

- [SKV 433 technical specification](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba55c/1766385913260/teknisk-beskrivning-skv-433-2026-utgava-36.pdf)
- [Official monthly tables](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba5af/1765287119989/allmanna-tabeller-manad.txt)
- [Worked examples](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba55f/1765284831853/bilaga-3-exempel-till-skv-433-2026.pdf)

## License

This project is distributed under the [MIT License](LICENSE).
