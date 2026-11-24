# Rust engine architecture

The supported calculator design statically links the `swedish-tax-ios`
library into Blazor's `dotnet.native.wasm`. C# calls the same current calculation
and editor C API as Swift on iOS. Rust is native WebAssembly code alongside the
.NET runtime; C# assemblies execute within that runtime.

```text
Blazor forms → RustTaxEngine → NativeCalculator → generated P/Invoke
                                                      ↓
                              Rust C ABI in dotnet.native.wasm
```

There is no JSON or application JavaScript calculation hop. Blazor uses
JavaScript for startup and DOM operations, and `RustTaxEngine` uses JavaScript
for local storage. GitHub Pages serves the standalone static entry page and
compiled assets. The normal build uses the ASP.NET host, which renders the entry
page and serves the same interactive app without prerendering it. Both modes share
the Rust calculation path; see [hosting](github-pages.md). All calculation inputs remain
in the browser. After loading, edits work without network access. This does
not imply offline navigation, a PWA, or loading the app directly via `file:`.

## Responsibilities

Rust supplies monthly tables, annual and mixed-income tax, payer withholding,
jämkning calibration, tax balances' underlying amounts, pension/SGI estimates,
and the complete preliminary dividend allowance. The full-plan result includes
pension and salary-exchange totals used in the authoritative projection.

The editor API supplies row proration, vacation suggestions and amounts,
pension benchmarks, exchange limits, plan totals and structured validation. C# maps
these results in `NativeCalculator`; Swift uses the same Rust support fields.
`NativeCalculator.Projection` derives display percentages, sums, annualized references
and balance traces from Rust results. See [FFI responsibilities and remaining duplication](ffi-responsibilities.md).

Invalid payment periods or excessive exchanges retain editor details and hide
the tax projection. The calculation C ABI supplies no partial withholding for an
invalid plan, so that state has an empty withholding result, as in the Swift
flow. The saved exchange is never silently reduced. Dividend validation is
independent and carries its own typed issue.

## ABI and ownership

`tests/RustPInvoke/generate-bindings.py` reads `SwedishTaxFFI.h` and generates
all 10 functions, constants and 21 structures into both the production
`SwedishTax.Native` assembly and the ABI harness. Its explicit C type subset
rejects unsupported declarations; `--check` detects drift.

Mappings use `uint`, `ulong`, `long`, `nuint`, `double`, typed pointers and
sequential layout. C boolean flags remain `uint`, preserving their 32-bit width.
`DllImport("libswedish_tax_ios")` matches the archive stem used to build Blazor's
P/Invoke table. Strings and descriptions are not part of the calculation ABI;
64-bit row IDs cross as integers without JavaScript conversion.

`NativeCalculator` links the current unversioned exports. It pins input arrays
during synchronous calls, copies Rust-owned support and withholding rows, and
frees each original result exactly once in `finally`. Pointer, length and capacity
remain intact for the matching
`swedish_tax_plan_support_free` or `swedish_tax_calculation_result_free`. No .NET
allocator frees Rust memory and no returned pointer survives cleanup.

The API and reference models have no version fields or compatibility dispatch. Every
consumer must use the current header and library together. The selected provider ref and generated
layout checks verify the selected source and declarations. JSON serializes saved inputs
and reads native reference fixtures; it is not the production calculation transport.

## Editing and persistence

Each edit clears stale results, captures and saves the workspace, and waits
120 ms to coalesce edits. A revision guard drops superseded work before the
synchronous native call. Child-row events refresh parent totals and results.
Reported FFI failures show no stale calculation and never select a fallback.
A failed runtime download requires reloading the page, as with Blazor startup.

Local storage key `swedish-tax.workspace` contains the table, age group and complete
plan. Derived results are not saved. Malformed workspaces and unreadable storage
are preserved until the user chooses to replace saved data. Storage failure does not
prevent calculation. The decoder reads current required fields and ignores unrelated
metadata without inspecting versions.
There are no old-schema migrations.

## Build and provider updates

`native-engine.json` selects the provider's `master` branch, target and Rust
flags, and the stable Rust channel and latest stable .NET SDK for CI.
`global.json` selects the highest installed stable SDK without a version pin.
CI checks out the current branch head. Local builds use the clean adjacent
checkout at its local `master`; update that checkout through Git before
building when new provider commits are available. The build does not fetch or
change the provider checkout.

The Rust build script selects the stable Rust channel by default and passes it
through to Cargo. An explicit `RUSTUP_TOOLCHAIN` must match that compiler and
include `rust-src` and the target. The build verifies generated declarations
and shared fixtures before building the host library and invoking
`cargo xtask wasm --release` for the WebAssembly static archive.

Artifacts use `artifacts/host`, `artifacts/rust`, `artifacts/xtask`, and
`artifacts/native`. .NET owns final linking and publication. The native
`build-info.json` records both `source_ref` and the resolved commit in
`revision`, plus the compiler. The checkout must remain clean and at that
commit throughout the build. Binary artifacts are not committed.

The supported build host is macOS or Linux. Install .NET SDK 11 or later and
the workloads restored from the application solution and ABI harness projects,
plus stable Rust with `rust-src` and `wasm32-unknown-emscripten`. The application and ABI harness
both target .NET 11 and use Blazor 11 packages. Verified tooling is SDK
11.0.100, WebAssembly workload/runtime and Blazor 11.0.0, and Emscripten 6.0.2.
See the root README for commands.
SDK feature-band upgrades require restoring workloads for the selected SDK and
rerunning both browser suites; the ABI harness records the actual SDK version.
Restore workloads from the projects so their tools match the target framework
when the selected SDK is newer than .NET 11.

Commit provider changes before building the consumer. The selected branch
supplies new engine commits automatically in CI; review and regenerate the
P/Invoke declarations and synchronize fixtures when the shared contract
changes. Run the .NET suite and both browser suites against that resolved
source. CI fails on declaration or fixture drift.

The iOS client selects its provider revision independently. Compare the actual
resolved commits when verifying cross-client parity, and rebuild its
XCFramework from the same source when the shared contract changes. Review the
provider, iOS and browser documentation together for those changes. The
provider owns fixture generation with `cargo xtask fixtures` and verifies it
with `cargo xtask fixtures --check`; see the
[shared fixture guide](https://github.com/qpernil/swedish-tax/blob/master/docs/shared-fixtures.md).

The ABI harness uses the same static archive as the application. It
adds only a test C layout probe and native-generated reference data. It is a
separate test project because those probes and fixtures do not ship in the UI.

## Toolchain constraints

Rust and its standard library use `target-cpu=mvp` with `bulk-memory-opt` and
`call-indirect-overlong` disabled for compatibility with older .NET optimizers that cannot read
those LLVM feature labels. Rust warns that these flags are unstable.
The provider's wasm task rebuilds the standard library with Cargo
`-Z build-std=std,panic_abort` and command-scoped `RUSTC_BOOTSTRAP=1`.
Its default flags match this configuration; the consumer explicitly passes the
flags from `native-engine.json` to keep the selected build reproducible. Toolchain upgrades require full parity
and browser verification; this is not a claim of arbitrary compiler compatibility.

`panic=abort` means an unexpected Rust panic terminates execution rather than
being caught by the iOS interface's `catch_unwind`. Normal invalid-input status
returns are covered. This build does not provide recoverable Rust panics.

## Verification

- .NET tests compare every field of 16 typed-plan fixtures with the native C ABI.
  The malformed-JSON rejection fixture remains in the DTO tests. Invalid plans
  explicitly allow the partial-withholding gap.
- The ABI harness compares 1,750 native cases, all 229 structure sizes/offsets,
  all 10 exports, null and invalid inputs, and 1,000 allocation/free cycles.
- The published app and harness are exercised in Chromium/Chrome, Firefox and
  WebKit, including offline execution. Mac verification used Chrome
  for Testing 156.0.8078.4, Playwright Firefox 157.0 and WebKit 27.2 with
  Playwright 1.64.1. WebKit testing does not claim physical iOS or shipping
  Safari verification.
- CI audits JavaScript and NuGet dependencies, checks the selected provider source and
  declarations, runs Rust and .NET tests, builds the hosted application, ABI
  harness and static Pages artifact, and tests
  all three in Chromium, Firefox and WebKit. Successful pushes and manual runs
  on `master` deploy the tested static files. Weekly Monday validation runs
  the same checks without uploading a Pages artifact or deploying; see
  [hosting](github-pages.md) for the schedule and deployment conditions.
