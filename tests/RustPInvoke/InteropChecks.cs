using System.Text.Json;

namespace RustPInvoke;

internal static unsafe class InteropChecks
{
    internal static string Run()
    {
        int layouts = LayoutChecks.Verify();
        using var stream = typeof(InteropChecks).Assembly.GetManifestResourceStream("native-cases.json")!;
        using var document = JsonDocument.Parse(stream);
        var reference = document.RootElement;
        NativeAssertions.Check(NativeTax.PlanningPolicy(), reference.GetProperty("policy"));
        int monthly = 0, annual = 0, profiles = 0, plans = 0;
        foreach (var row in reference.GetProperty("cases").EnumerateArray())
        {
            var result = NativeTax.MonthlyDeduction(row[0].GetUInt32(), row[1].GetUInt32(), row[2].GetUInt32());
            if (result.status != row[3].GetUInt32() || result.kind != row[4].GetUInt32() || result.value != row[5].GetUInt32())
                throw new InvalidOperationException($"Monthly deduction mismatch: {row}");
            monthly++;
        }
        foreach (var row in reference.GetProperty("annualCases").EnumerateArray())
        {
            NativeAssertions.Check(NativeTax.AnnualTax(row.GetProperty("table").GetUInt32(), row.GetProperty("column").GetUInt32(), row.GetProperty("income").GetUInt32()), row.GetProperty("result"));
            annual++;
        }
        foreach (var row in reference.GetProperty("profileCases").EnumerateArray())
        {
            NativeAssertions.Check(NativeTax.AnnualTaxForIncomeProfile(row.GetProperty("table").GetUInt32(), row.GetProperty("age").GetUInt32(), row.GetProperty("work").GetUInt32(), row.GetProperty("pension").GetUInt32()), row.GetProperty("result"));
            profiles++;
        }
        foreach (var row in reference.GetProperty("planCases").EnumerateArray())
        {
            CheckPlan(row, repetitions: 1);
            plans++;
        }
        CheckPlan(reference.GetProperty("planCases")[0], repetitions: 1000);

        var nullResult = NativeTax.CalculatePlan(null);
        try
        {
            if (nullResult.status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT)
                throw new InvalidOperationException("Null plan request must return InvalidInput");
        }
        finally { NativeTax.CalculationResultFree(nullResult); }
        if (NativeTax.DividendAllowanceForPlan(null).status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT
            || NativeTax.AnnualTax(32, 0, 660000).status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT
            || NativeTax.AnnualTaxForIncomeProfile(32, 2, 660000, 0).status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT)
            throw new InvalidOperationException("Invalid scalar inputs must return InvalidInput");

        var nullSupport = NativeTax.PlanSupport(null);
        try
        {
            if (nullSupport.status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT
                || NativeTax.EntrySupport(null).status != NativeTax.SWEDISH_TAX_STATUS_INVALID_INPUT)
                throw new InvalidOperationException("Null support request must return InvalidInput");
        }
        finally { NativeTax.PlanSupportFree(nullSupport); }

        return $"Passed all 10 FFI functions: {layouts} C ABI layout checks; {monthly} monthly, {annual} annual, {profiles} mixed-income and {plans} plan/dividend/support reference cases; 1000 allocation/free cycles; null and invalid inputs.";
    }

    private static void CheckPlan(JsonElement row, int repetitions)
    {
        var input = row.GetProperty("request");
        var entries = input.GetProperty("entries").EnumerateArray()
            .Select(NativeFixtureInputs.ReadSwedishTaxIncomeEntry).ToArray();
        var request = NativeFixtureInputs.ReadSwedishTaxPlanRequest(input);
        fixed (NativeTax.SwedishTaxIncomeEntry* pinned = entries)
        {
            request.entries = pinned;
            request.entries_count = checked((nuint)entries.Length);
            for (int iteration = 0; iteration < repetitions; iteration++)
            {
                // Rust borrows the pinned input only for the duration of the call.
                var result = NativeTax.CalculatePlan(&request);
                try
                {
                    // Inspect Rust-owned rows while their allocation is still alive.
                    NativeAssertions.Check(result, row.GetProperty("calculation"));
                }
                finally
                {
                    // Return the original pointer/count/capacity to Rust exactly once.
                    NativeTax.CalculationResultFree(result);
                }
                var support = NativeTax.PlanSupport(&request);
                try { NativeAssertions.Check(support, row.GetProperty("support")); }
                finally { NativeTax.PlanSupportFree(support); }
                for (int i = 0; i < entries.Length; i++)
                    NativeAssertions.Check(NativeTax.EntrySupport(pinned + i), row.GetProperty("entrySupport")[i]);
                NativeAssertions.Check(NativeTax.DividendAllowanceForPlan(&request), row.GetProperty("dividend"));
            }
        }
    }
}
