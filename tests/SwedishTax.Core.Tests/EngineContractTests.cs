using System.Text.Json;
using System.Text.Json.Nodes;
using SwedishTax.Core;

namespace SwedishTax.Core.Tests;

public class EngineContractTests
{
    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.json").Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryRustFixtureDeserializesWithoutDroppingFields(string path)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(path))!;
        var response = EngineJson.Response(fixture["response"]!.ToJsonString());
        // Compare the complete round-tripped response, including every nested derived field.
        Assert.True(JsonNode.DeepEquals(fixture["response"], JsonSerializer.SerializeToNode(response, EngineJson.Options)), path);
        if (Path.GetFileName(path) is "unknown_field.json") return;
        var request = JsonSerializer.Deserialize<PlanRequest>(fixture["request"]!.ToJsonString(), EngineJson.Options)!;
        Assert.True(JsonNode.DeepEquals(fixture["request"], JsonSerializer.SerializeToNode(request, EngineJson.Options)), path);
    }

    [Fact]
    public void WorkspaceRoundTripPreservesEveryEditableInputAndStableIdentifiers()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/complete.json")))!;
        var request = JsonSerializer.Deserialize<PlanRequest>(fixture["request"]!.ToJsonString(), EngineJson.Options)!;
        var workspace = new SavedWorkspace(request.Table, request.AgeGroup, request.Plan);
        var restored = SavedWorkspace.Restore(workspace.Serialize());
        Assert.Equal(workspace.Serialize(), restored.Serialize());
        Assert.DoesNotContain("result", workspace.Serialize());
        var maxId = restored.Plan.Entries.Max(e => e.Id);
        var added = restored.Plan.AddEntry(IncomeKind.OneTimeSalary);
        Assert.True(added > maxId);
        restored.Plan.RemoveEntry(added);
        Assert.Equal(workspace.Serialize(), restored.Serialize());
        Assert.NotNull(restored.Plan.Entries[1].SalaryExchange?.PreviousYearPensionSalaryBasis);
    }

    [Fact]
    public void MalformedFieldsAndMissingRequiredResultsFailVisibly()
    {
        Assert.Throws<JsonException>(() => EngineJson.Response("{}"));
        Assert.Throws<JsonException>(() => EngineJson.Response("{\"issue\":null,\"result\":null}"));
        Assert.Throws<JsonException>(() => EngineJson.Response("{\"issue\":null,\"result\":null,\"surprise\":0}"));
        Assert.Throws<JsonException>(() => SavedWorkspace.Restore("{\"table\":32,\"age_group\":\"Under66AtYearStart\"}"));
        Assert.Throws<JsonException>(() => EngineJson.Response("{\"issue\":{\"kind\":\"UnknownError\",\"entry_id\":null,\"maximum\":null},\"result\":null}"));
    }

    [Fact]
    public void WorkspaceReadsCurrentFieldsWithoutInterpretingExtraMetadata()
    {
        var workspace = new SavedWorkspace(32, TaxAgeGroup.Under66AtYearStart, IncomePlan.WithMonthlySalary(55000));
        var saved = JsonNode.Parse(workspace.Serialize())!;
        saved["extra_metadata"] = "ignored";
        Assert.Equal(workspace.Serialize(), SavedWorkspace.Restore(saved.ToJsonString()).Serialize());
    }

    [Fact]
    public void EditingInvalidatesEveryOlderCalculationRevision()
    {
        var revisions = new CalculationRevision();
        var first = revisions.Next();
        var second = revisions.Next();
        Assert.False(revisions.IsCurrent(first));
        Assert.True(revisions.IsCurrent(second));
        revisions.Next();
        Assert.False(revisions.IsCurrent(second));
    }
}
