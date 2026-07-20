namespace SwedishTax.Core;

public sealed record AnnualTax(
    uint AssessedIncome,
    uint BasicAllowance,
    uint TaxableIncome,
    uint StateIncomeTax,
    uint MunicipalIncomeTax,
    uint BurialAndReligiousFee,
    uint PensionFee,
    uint PensionFeeCredit,
    uint WorkIncomeCredit,
    uint SicknessCompensationCredit,
    uint EarnedIncomeCredit,
    uint PublicServiceFee,
    uint Total);
