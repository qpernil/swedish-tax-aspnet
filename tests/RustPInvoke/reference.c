#include "SwedishTaxFFI.h"
#include <inttypes.h>
#include <stdio.h>
#include "reference_serializers.h"

static void print_case(uint32_t table, uint32_t column, uint32_t income, int *first) {
    struct SwedishTaxDeductionResult result = swedish_tax_monthly_deduction(table, column, income);
    printf("%s[%" PRIu32 ",%" PRIu32 ",%" PRIu32 ",%" PRIu32 ",%" PRIu32 ",%" PRIu32 "]",
           *first ? "" : ",", table, column, income, result.status, result.kind, result.value);
    *first = 0;
}

static SwedishTaxOptionalU32 some(uint32_t value) {
    return (SwedishTaxOptionalU32){1, value};
}

static void print_plan(uint32_t table, uint32_t age, uint32_t variant, uint32_t adjustment, int *first) {
    SwedishTaxIncomeEntry entries[6] = {0};
    size_t count = variant == 5 ? 0 : variant == 1 ? 6 : variant == 2 || variant >= 7 ? 2 : 1;
    for (size_t i = 0; i < count; i++) {
        entries[i].id = UINT64_MAX - i;
        entries[i].kind = variant == 1 ? (uint32_t)i : 1;
        entries[i].amount = variant == 1 ? (i == 0 || i == 4 ? 120000 : i == 5 ? 100000 : 5000) : 55033;
        entries[i].start = (SwedishTaxDate){1, 1};
        entries[i].end = (SwedishTaxDate){12, 31};
        entries[i].own_company_sourced = i == 0;
        entries[i].payer_role = i == 0 ? 0 : 1;
        entries[i].included_in_pension_salary_basis = 1;
        entries[i].adjustment_applies = adjustment != 0;
        entries[i].use_full_year_projection_as_adjustment_basis = adjustment != 0;
    }
    entries[0].regular_pension_premium = (SwedishTaxRegularPensionPremium){1, some(2500)};
    if (variant == 1) {
        entries[1].actual_withholding = some(12000);
        entries[2].additional_withholding_per_payment = some(500);
        entries[3].start = (SwedishTaxDate){3, 15};
        entries[3].use_annual_daily_rate_for_partial_months = 1;
    }
    if (variant == 2) {
        entries[0].amount = 93000;
        entries[0].end = (SwedishTaxDate){10, 18};
        entries[0].use_annual_daily_rate_for_partial_months = 1;
        entries[0].vacation_compensation = (SwedishTaxVacationCompensation){1, 30, 20, 500, 1, some(3500)};
        entries[1].kind = 2;
        entries[1].amount = 372000;
        entries[1].salary_exchange = (SwedishTaxSalaryExchange){1, 211000, 1, 580, {1, 1092000}, {1, 158170}};
    }
    if (variant == 6) { entries[0].start.month = 12; entries[0].end.month = 1; }
    if (variant >= 7) {
        entries[0].amount = variant == 8 ? UINT32_MAX : 93000;
        entries[1].kind = 2;
        entries[1].amount = variant == 8 ? UINT32_MAX : 372000;
        entries[1].salary_exchange = (SwedishTaxSalaryExchange){1, UINT32_MAX, 1, 576, {0, 0}, {0, 0}};
        if (variant == 9) {
            entries[1].salary_exchange.previous_year_pension_salary_basis = some(1092000);
            entries[1].salary_exchange.pension_and_insurance_costs_before_exchange = some(158170);
        }
    }
    SwedishTaxPlanRequest request = {
        .table = table, .age_group = age,
        .entries = entries, .entries_count = count,
        .adjustment_percent = {adjustment != 0, adjustment},
        .dividend_allowance = {
            .one_person_company = 1, .ownership_basis_points = 10000,
            .acquisition_cost = 150000, .acquisition_cost_interest_basis_points = {1, 1196}, .saved_allowance = 20000
        }
    };
    if (variant == 3) request.age_group = 2;
    if (variant == 4) request.dividend_allowance.ownership_basis_points = 11000;
    if (!*first) putchar(',');
    *first = 0;
    printf("{\"request\":");
    print_SwedishTaxPlanRequest(request);
    printf(",\"calculation\":");
    SwedishTaxCalculationResult result = swedish_tax_calculate_plan(&request);
    print_SwedishTaxCalculationResult(result);
    swedish_tax_calculation_result_free(result);
    printf(",\"support\":");
    SwedishTaxPlanSupport support = swedish_tax_plan_support(&request);
    print_SwedishTaxPlanSupport(support);
    swedish_tax_plan_support_free(support);
    printf(",\"entrySupport\":[");
    for (size_t i = 0; i < count; i++) {
        if (i) putchar(',');
        print_SwedishTaxEntrySupport(swedish_tax_entry_support(&entries[i]));
    }
    printf("],\"dividend\":");
    print_SwedishTaxDividendAllowanceResult(swedish_tax_dividend_allowance_for_plan(&request));
    putchar('}');
}

int main(void) {
    const uint32_t incomes[] = {0, 1, 1999, 2000, 2001, 10000, 35000, 55000, 79999, 80000, 80001, 100000, 1000000, UINT32_MAX};
    int first = 1;
    printf("{\"cases\":[");
    for (uint32_t table = 29; table <= 42; table++)
        for (uint32_t column = 1; column <= 6; column++)
            for (unsigned i = 0; i < sizeof(incomes) / sizeof(incomes[0]); i++)
                print_case(table, column, incomes[i], &first);
    print_case(28, 1, 55000, &first);
    print_case(43, 1, 55000, &first);
    print_case(300, 1, 55000, &first);
    print_case(32, 0, 55000, &first);
    print_case(32, 7, 55000, &first);
    print_case(32, UINT32_MAX, 55000, &first);
    printf("],\"annualCases\":[");
    first = 1;
    const uint32_t annual_incomes[] = {0, 24000, 660000, 1200000};
    for (uint32_t table = 29; table <= 42; table++) {
        for (uint32_t column = 1; column <= 6; column++) {
            for (unsigned i = 0; i < sizeof(annual_incomes) / sizeof(annual_incomes[0]); i++) {
                printf("%s{\"table\":%" PRIu32 ",\"column\":%" PRIu32 ",\"income\":%" PRIu32 ",\"result\":", first ? "" : ",", table, column, annual_incomes[i]);
                first = 0;
                print_SwedishTaxAnnualTaxResult(swedish_tax_annual_tax(table, column, annual_incomes[i]));
                putchar('}');
            }
        }
    }
    printf("],\"profileCases\":[");
    first = 1;
    for (uint32_t table = 29; table <= 42; table++) {
        for (uint32_t age = 0; age <= 1; age++) {
            for (uint32_t work = 0; work <= 660000; work += 660000) {
                for (uint32_t pension = 0; pension <= 360000; pension += 360000) {
                    printf("%s{\"table\":%" PRIu32 ",\"age\":%" PRIu32 ",\"work\":%" PRIu32 ",\"pension\":%" PRIu32 ",\"result\":", first ? "" : ",", table, age, work, pension);
                    first = 0;
                    print_SwedishTaxAnnualTaxResult(swedish_tax_annual_tax_for_income_profile(table, age, work, pension));
                    putchar('}');
                }
            }
        }
    }
    printf("],\"planCases\":[");
    first = 1;
    const uint32_t tables[] = {29, 32, 42};
    for (unsigned i = 0; i < sizeof(tables) / sizeof(tables[0]); i++)
        for (uint32_t age = 0; age <= 1; age++)
            for (uint32_t variant = 0; variant <= 9; variant++)
                for (uint32_t adjustment = 0; adjustment <= 25; adjustment += 25)
                    print_plan(tables[i], age, variant, adjustment, &first);
    printf("],\"policy\":");
    print_SwedishTaxPlanningPolicy(swedish_tax_planning_policy());
    puts("}");
    return 0;
}
