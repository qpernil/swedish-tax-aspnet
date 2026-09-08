# Rust C interface through Blazor P/Invoke

This browser ABI verification harness links the existing `swedish-tax-ios` static
library into Blazor's `dotnet.native.wasm`. C# calls all 10 C exports using
P/Invoke: six calculation/cleanup functions and four planning-support functions.
The harness includes a small C probe for layout measurements. Calculation calls pass C-compatible structures directly to Rust.

The .NET runtime executes the C# assemblies; Rust is linked native WebAssembly
code alongside that runtime. Blazor still uses JavaScript to start the runtime
and operate the DOM. A static HTTP server suffices for this harness.

## Build and verify

Verified on macOS ARM64 with Rust 1.98.1, .NET SDK 10.0.301, runtime/workload
packs 10.0.9 and the workload's Emscripten 3.1.56 tools. Install `rust-src`, the
`wasm32-unknown-emscripten` Rust target, and the `wasm-tools` .NET workload in the
SDK used for the build. The default Rust source is the adjacent `swedish-tax`
checkout. The build reads its existing C interface and generated header.

From the repository root:

```sh
rustup toolchain install 1.98.1 --component rust-src --target wasm32-unknown-emscripten
dotnet workload install wasm-tools --skip-manifest-update
RUSTUP_TOOLCHAIN=1.98.1 python3 tests/RustPInvoke/build.py
python3 -m http.server 5182 --bind 127.0.0.1 --directory tests/RustPInvoke/bin/pinvoke-publish/wwwroot
```

An isolated SDK can be selected with `--dotnet /path/to/dotnet`. Use
`--rust-source /path/to/swedish-tax` and `--output /path/to/publish` to override
the source and output locations. To select a separately installed, pinned Rust
toolchain, set `RUSTUP_TOOLCHAIN=1.98.1` for the build. Generated libraries,
reference data and published files live in ignored `obj`/`bin` directories.
The output contains `build-info.json` recording the provider revision, dirty
state, compiler versions and reference-case counts.

With the repository's Playwright dependency installed, run in another terminal:

```sh
PLAYWRIGHT_CHANNEL=chrome node tests/RustPInvoke/smoke.cjs
PLAYWRIGHT_BROWSER=firefox node tests/RustPInvoke/smoke.cjs
PLAYWRIGHT_BROWSER=webkit node tests/RustPInvoke/smoke.cjs
```

Install the other test engines with `npx playwright install firefox webkit`.
Omit `PLAYWRIGHT_CHANNEL` to use Playwright's bundled Chromium. `PINVOKE_URL`
overrides the default `http://127.0.0.1:5182`; `PINVOKE_SCREENSHOT` optionally
saves a screenshot.

The build compiles a native C reference program against the same Rust library.
The browser compares 1,750 native reference cases:

- 1,182 monthly cases: tables 29–42, columns 1–6, 14 income values including
  zero, band boundaries and `UINT32_MAX`, plus invalid table/column inputs;
- 336 annual cases and 112 mixed-income cases;
- 120 plan/dividend/support cases covering both ages, adjustment calibration, all six
  income kinds, partial dates, pension premiums, vacation compensation, salary
  exchange, excessive saved requests, saturation, confirmed costs, invalid periods,
  empty plans, invalid age inputs and dividend ownership errors.

Every field of the returned structures is compared, including nested structs,
negative signed adjustments, doubles and 64-bit entry IDs above JavaScript's
exact integer range. Allocator capacity and pointer addresses are excluded from
value comparisons because their values can differ across platforms. Tests also
check null request handling and 1,000 repeated allocation/free cycles.

The browser verifies 229 C/C# layout measurements: every struct size and every
field offset. It repeats the checks with the network disabled and verifies
that one combined native WASM module loads.
JSON in this harness is only for loading native-generated test fixtures;
the calculation calls themselves pass C-compatible structures directly.

The complete published-build suite passes in Chrome 152.0.7977.83, Playwright
Firefox 153.0 and Playwright WebKit 26.5 on this Mac. The WebKit run exercises
that engine; it is not a separate verification on an iPhone or shipping Safari.

## Generated declarations and ownership

`NativeTax.g.cs` declares all functions, constants and 21 structs from the C
header. `generate-bindings.py` supports the header's explicit C type subset and
rejects unsupported fields or function signatures. It also generates layout
checks, a native layout probe, fixture serializers and field comparisons. The
build checks for drift against the selected provider's generated header.

```sh
python3 tests/RustPInvoke/generate-bindings.py
python3 tests/RustPInvoke/generate-bindings.py --check
```

The mappings are `uint32_t` → `uint`, `uint64_t` → `ulong`, `int64_t` → `long`,
`size_t` → `nuint`, `double` → `double`, and C pointers → typed unsafe pointers.
All structs use sequential layout with the platform's default alignment. The
contract's boolean-like flags stay 32-bit integers; they are not C# `bool`s.

`InteropChecks.CheckPlan` demonstrates the ownership contract: pin the managed
entry array during the synchronous call, pass the request by pointer, inspect
the Rust-owned output while it is alive, and call
`swedish_tax_calculation_result_free` and `swedish_tax_plan_support_free`
exactly once for their respective results in `finally`. Preserve the
original pointer, count and capacity for cleanup. Never free that buffer with
a .NET allocator or use it after cleanup. These low-level tests exercise raw
declarations. The application uses
`src/SwedishTax.Native/NativeCalculator.cs` as its managed ownership wrapper.

## Compatibility constraints and scope

- `[DllImport("libswedish_tax_ios")]` must match the static archive's filename
  stem, including `lib`, because Blazor generates its P/Invoke table from that
  name. It does not apply a desktop shared-library name lookup here.
- Rust and its standard library are rebuilt for the Emscripten target with
  `target-cpu=mvp` and the LLVM `bulk-memory-opt` and `call-indirect-overlong`
  features disabled. The optimizer bundled with this .NET workload cannot read
  those newer feature labels. The pinned compiler warns that the two LLVM
  feature flags are unstable.
- Rebuilding the standard library uses experimental Cargo `-Z build-std` with
  `RUSTC_BOOTSTRAP=1` scoped to that command. The application and harness use the
  same pinned, tested configuration;
  compiler upgrades require repeating the full verification.
- The WebAssembly library uses `panic=abort`. Unexpected Rust panics terminate
  execution instead of being converted by the iOS interface's `catch_unwind`
  into an internal-error status. Normal invalid-input status returns are tested.
- Verification covers all current FFI entry points, their layouts and the
  documented cases. It does not establish compatibility for every possible
  input, panic recovery, unrelated Rust runtime facilities or future toolchains.
- The harness is outside `SwedishTax.slnx` so its C probes and reference data do
  not ship in the calculator. CI builds and runs it separately alongside the
  normal application tests.

The production architecture is documented in
[the engine guide](../../docs/rust-engine.md). The harness build
invokes `scripts/build-native-engine.py`, which calls the provider's
`cargo xtask wasm` and supplies the same static archive used by the application. That script requires a clean provider at the pinned revision
and checks the production declarations and shared fixtures as well.
