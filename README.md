# Swedish tax calculations for 2026

## [Open the live calculator →](https://qpernil.github.io/swedish-tax-aspnet/)

**Runs entirely in your browser. No installation required.**
Published to GitHub Pages from the artifact built and tested by CI.
[Hosting and local preview](docs/github-pages.md).

This ASP.NET Core Blazor WebAssembly application uses the authoritative
[`swedish-tax`](https://github.com/qpernil/swedish-tax) Rust engine for
Skatteverket monthly tables 29–42 and the annual preliminary-tax formulas from
SKV 433, edition 36, for income year 2026. Calculations use the published-table
assumptions and are not an individualized final tax assessment.

## Web application

The calculator supports complete annual plans with:

- annual and date-prorated monthly salary and occupational pension;
- one-time salary, termination payments, and own-company dividends;
- main/secondary payers, percentage jämkning, actual withholding, and voluntary extra withholding per payment;
- calendar-day or annual daily-rate proration for partial months;
- editable vacation days and compensation rates (default 5.4% per paid day);
- estimated or entered occupational-pension contributions and salary exchange;
- current-year or preceding-year pensionable salary bases and confirmed pension/insurance costs;
- mixed-income annual tax, jämkning calibration, withholding reconciliation, and PGI/SGI estimates; and
- the complete preliminary 2027 dividend allowance, including payroll, ownership, wage caps, acquisition-cost interest, and saved allowance.

Blazor runs the C# interface in the browser. The shared `swedish-tax-ios`
Rust library is statically linked into `dotnet.native.wasm`; C# calls its
current calculation and editor C API through generated P/Invoke declarations.
Swift and C# use the same Rust interface. The normal build includes an ASP.NET
Core host. The Pages publish workflow selects the standalone static entry page;
both serve the same calculator and compiled browser assets.
There is no calculation API and no financial data is sent to the server.

Rust supplies tax, withholding, dividend results and editor support: proration,
vacation/pension previews, exchange allowances, invalid-plan totals and structured
validation. Both clients map the same native results. Excessive saved exchanges
remain unchanged while allowance previews use the permitted maximum. See
[FFI responsibilities and remaining duplication](docs/ffi-responsibilities.md).

The complete input plan, tax table, and age selection are saved in this
browser's local storage after edits. Browser storage is specific to the origin
and browser profile. Clearing site data removes the saved plan; private browsing
or storage restrictions can prevent saving. Unreadable or malformed saved
plans are preserved until the user explicitly replaces them.

## Projects

- `src/SwedishTax.Core`: editable inputs, result models, saved-workspace JSON,
  and display arithmetic over Rust results.
- `src/SwedishTax.Native`: generated C ABI declarations and the managed
  calculation wrapper, including Rust buffer cleanup.
- `src/SwedishTax.Web/SwedishTax.Web`: ASP.NET host and static assets.
- `src/SwedishTax.Web/SwedishTax.Web.Client`: shared Blazor interface with hosted
  and standalone static startup. JavaScript
  interop in the application service is limited to local storage.
- `tests/SwedishTax.Core.Tests`: native calculation parity, reference DTOs,
  saved inputs, and calculation revisions.
- `tests/fixtures`: 17 checked-in copies of the pinned Rust provider's input/result
  fixtures; the native-engine build requires an exact match.
- `tests/browser`: published calculator workflows in Chromium, Firefox and WebKit.
- `tests/RustPInvoke`: all 10 C exports, 21 structure layouts, native/browser
  reference comparisons, and allocation/cleanup tests.

See [the engine architecture](docs/rust-engine.md) for ownership,
compatibility, supported tooling and the provider-update workflow.

## Build and run

Use .NET SDK 10.0.301 with its `wasm-tools` workload and Rust 1.98.1. The build
requires a clean `swedish-tax` checkout at the revision in `native-engine.json`;
the default location is adjacent to this repository. Generated libraries live
in ignored `artifacts/` directories. `scripts/build-native-engine.py` verifies
the pin, declarations and fixture copies, builds the host test library, and invokes
the provider's `cargo xtask wasm` to compile the WebAssembly C library.
`dotnet build`/`publish` link that library into the browser runtime.

```sh
rustup toolchain install 1.98.1 --component rust-src --target wasm32-unknown-emscripten
dotnet workload install wasm-tools --skip-manifest-update
RUSTUP_TOOLCHAIN=1.98.1 python3 scripts/build-native-engine.py --source ../swedish-tax
dotnet restore SwedishTax.slnx
dotnet build SwedishTax.slnx -c Release
dotnet test SwedishTax.slnx -c Release
dotnet run --project src/SwedishTax.Web/SwedishTax.Web
```

The application prints its local URL. For a static preview, see
[GitHub Pages hosting](docs/github-pages.md). To verify the ASP.NET host:

```sh
dotnet publish src/SwedishTax.Web/SwedishTax.Web -c Release -o /tmp/swedish-tax-publish
cd /tmp/swedish-tax-publish
dotnet SwedishTax.Web.dll --urls http://127.0.0.1:5181
```

From this repository, install the browser-test dependency and run against the
published application:

```sh
pnpm install --frozen-lockfile
pnpm exec playwright install chromium firefox webkit
pnpm test:browser
PLAYWRIGHT_BROWSER=firefox pnpm test:browser
PLAYWRIGHT_BROWSER=webkit pnpm test:browser
```

`TAX_APP_URL` overrides the default `http://127.0.0.1:5181`.
`PLAYWRIGHT_CHANNEL=chrome` selects installed Chrome. The suite checks loaded
native WASM, fixture results, offline edits without requests, saved plans,
validation, mobile layout, and storage failures. See the
[ABI harness](tests/RustPInvoke/README.md) for lower-level verification.

The pinned toolchain rebuilds Rust's standard library using experimental Cargo
`build-std`, with `RUSTC_BOOTSTRAP` scoped to that build. Rust panics abort the
browser runtime instead of returning an FFI error. These remain compatibility
constraints of the supported design; ordinary invalid inputs return errors.

## Sources

The Rust repository maintains the calculation sources and business-rule tests.
The underlying 2026 references include:

- [SKV 433 technical specification](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba55c/1766385913260/teknisk-beskrivning-skv-433-2026-utgava-36.pdf)
- [Official monthly tables](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba5af/1765287119989/allmanna-tabeller-manad.txt)
- [Worked examples](https://www.skatteverket.se/download/18.1522bf3f19aea8075ba55f/1765284831853/bilaga-3-exempel-till-skv-433-2026.pdf)

## License

This project is distributed under the [MIT License](LICENSE).
