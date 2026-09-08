# FFI responsibilities and remaining duplication

Swift and C# use the same current calculation and editor-support C API. Rust supplies
tax-table lookup, annual tax, withholding, income bases,
marginal rates, jämkning calibration, dividend allowance and planning/editor rules.
The [provider contract](https://github.com/qpernil/swedish-tax/blob/master/docs/ffi-plan-support.md)
and generated `SwedishTaxFFI.h` define layouts, ownership and invalid-plan semantics.

| Responsibility | Rust source | Client use |
| --- | --- | --- |
| Proration, monthly amounts, payment counts | `IncomeEntry` | Copied support fields; Swift standalone entry preview |
| Pension benchmark and regular contributions | `RegularPensionPremium`, `IncomeEntry` | Copied row fields |
| Vacation suggestion, payout, daily value and pension premium | `VacationCompensation`, `IncomeEntry` | Copied row fields; Swift suggestions use entry preview |
| Exchange contribution, basis, ceiling and binary-search maximum | `SalaryExchangeContext::allowance_for` | Copied allowance fields |
| Work, pension, dividend, SGI and employer contribution totals | `IncomePlan::totals` | Available even for invalid plans |
| Invalid period and excessive saved exchange | `IncomePlan::validation_issue` | Structured issue, 64-bit row ID and maximum |
| Uniform monthly reference and tax columns | `IncomePlan`, `TaxAgeGroup` | Browser support fields; Swift presentation column enum mapping |
| New-input defaults and dividend acquisition-cost threshold | Rust policy constants | Policy export for interactive defaults and Swift threshold |

## Preview and validation semantics

An excessive exchange stays saved exactly as entered. Plan and row totals retain
those inputs, with the entry sacrifice bounded by its payment. The allowance preview
uses the permitted maximum for its contribution, remaining pension basis and ceiling.
Validation checks the saved request, including amounts above the entire payment.
Both clients suppress the annual projection until corrected. Invalid periods take
precedence over excessive exchanges, followed by input row order.

Saturated totals use Rust's ordering: reconstruct the selected payment's basis as
aggregate basis minus that entry's basis plus its payment basis. Clients do not
reconstruct this themselves. Confirmed costs and previous-year bases are passed through
as optional inputs. Invalid plans retain editor details but the calculation ABI returns
no partial withholding; both clients show an empty withholding result in that state.

## Client responsibilities and remaining arithmetic

`NativeCalculator` maps and frees native results. `RustTaxCore` maps Swift row and plan
support. Neither client implements proration, vacation calculations, pension benchmark
rates, exchange binary search, plan aggregation or business validation.

Persistence, stable IDs, enum/date widgets, SEK formatting and percentage input conversion
remain client responsibilities. C# input models contain constructor defaults;
interactive editor creation uses Rust policy. Simple display arithmetic over
Rust final-tax results remains in `NativeCalculator.Projection`, `DisplayArithmetic` and
Swift `PlanCalculation`/`IncomePlanTotals` properties (sums, net cash, annualization,
percentages and tax-balance traces). Swift also retains its display column mapping and
both clients retain explanatory year-specific text. These are the remaining duplication
boundaries, not another tax or editor-rule implementation.

## Current interface and verification

The C API has one supported layout and unversioned export names. Provider and clients
are built together; there is no version negotiation, legacy ABI or migration dispatch.
Generated declarations, pinned provider source and layout tests enforce the build contract.
The browser uses P/Invoke with Rust statically linked into `dotnet.native.wasm`.
Rust-owned buffers are copied and returned exactly once to their matching Rust free function.

Shared fixtures cover excessive exchange requests, payment caps, prior-year bases,
confirmed costs and saturation. The browser harness checks all 10 exports, 229 sizes/
offsets and every returned field against native-generated cases. Swift simulator tests
cover clamped previews, saved-input round trips, preservation of unreadable files,
invalid rows, repeated copied buffers and ARM64 layouts for the six editor-support
structures. See the
[engine guide](rust-engine.md) for the maintained build and test workflow.
