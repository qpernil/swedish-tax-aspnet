using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwedishTax.Core;

public static class EngineJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string Request(byte table, TaxAgeGroup ageGroup, IncomePlan plan) =>
        JsonSerializer.Serialize(new PlanRequest(table, ageGroup, plan), Options);

    public static EngineResponse Response(string json)
    {
        var response = JsonSerializer.Deserialize<EngineResponse>(json, Options)
            ?? throw new JsonException("The tax engine returned an empty response.");
        if (response.Result is null && response.Issue is null)
            throw new JsonException("The tax engine returned neither a result nor an issue.");
        return response;
    }
}

public sealed record SavedWorkspace(byte Table, TaxAgeGroup AgeGroup, IncomePlan Plan)
{
    // Decode the current fields without interpreting unrelated JSON metadata.
    // Required fields and value validation still reject malformed workspaces.
    private static readonly JsonSerializerOptions ReadOptions = new(EngineJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
    public string Serialize() => JsonSerializer.Serialize(this, EngineJson.Options);
    public static SavedWorkspace Restore(string json)
    {
        var value = JsonSerializer.Deserialize<SavedWorkspace>(json, ReadOptions)
            ?? throw new JsonException("The saved workspace is empty.");
        if (value.Plan is null || value.Plan.Entries is null || value.Plan.DividendAllowance is null
            || value.Plan.Entries.Count is 0 or > 1000 || value.Plan.Entries.Any(e => e is null)
            || value.Table is < 29 or > 42)
            throw new JsonException("The saved workspace is invalid.");
        return value;
    }
}

/// <summary>Only the newest requested calculation may update the screen.</summary>
public sealed class CalculationRevision
{
    private long current;
    public long Next() => ++current;
    public bool IsCurrent(long revision) => revision == current;
}
