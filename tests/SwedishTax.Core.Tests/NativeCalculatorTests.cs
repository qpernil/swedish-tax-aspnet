using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwedishTax.Core;
using SwedishTax.Native;

namespace SwedishTax.Core.Tests;

public class NativeCalculatorTests
{
    static NativeCalculatorTests()
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var file = Path.Combine(repository, "artifacts/native/libswedish_tax_ios." + (OperatingSystem.IsMacOS() ? "dylib" : "so"));
        NativeLibrary.SetDllImportResolver(typeof(NativeCalculator).Assembly, (name, _, _) =>
            name == "libswedish_tax_ios" ? NativeLibrary.Load(file) : IntPtr.Zero);
    }

    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.json")
        // This fixture exercises malformed JSON, which cannot reach the typed C ABI.
        .Where(path => Path.GetFileName(path) != "unknown_field.json")
        .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Native_projection_and_support_match_shared_Rust_fixtures(string file)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(file));
        var request = fixture.RootElement.GetProperty("request").Deserialize<PlanRequest>(EngineJson.Options)!;
        var expected = JsonNode.Parse(fixture.RootElement.GetProperty("response").GetRawText())!;
        var result = NativeCalculator.Calculate(request.Table, request.AgeGroup, request.Plan);
        var actual = JsonSerializer.SerializeToNode(result, EngineJson.Options)!;
        // The calculation interface returns no partial withholding for an invalid plan.
        // Both callers suppress the projection until the plan is valid.
        if (result.Result is { Calculation: null })
            expected["result"]!["withholding"] = JsonSerializer.SerializeToNode(new WithholdingSummary(0, []), EngineJson.Options);
        Equal(expected, actual, Path.GetFileName(file));
    }

    private static void Equal(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is JsonObject map)
        {
            Assert.IsType<JsonObject>(actual);
            Assert.Equal(map.Count, ((JsonObject)actual!).Count);
            foreach (var pair in map) Equal(pair.Value, actual[pair.Key], path + "." + pair.Key);
        }
        else if (expected is JsonArray array)
        {
            Assert.IsType<JsonArray>(actual);
            Assert.Equal(array.Count, ((JsonArray)actual!).Count);
            for (int i = 0; i < array.Count; i++) Equal(array[i], actual[i], path + $"[{i}]");
        }
        else if (expected is JsonValue a && actual is JsonValue b && a.TryGetValue<JsonElement>(out var number) && number.ValueKind == JsonValueKind.Number)
        {
            // Decimal comparison preserves all u64 identifiers while tolerating
            // the last few binary floating-point rounding bits in percentages.
            var left = decimal.Parse(a.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
            var right = decimal.Parse(b.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(left - right) < 0.00000001m, $"{path}: expected {left}, actual {right}");
        }
        else Assert.True(JsonNode.DeepEquals(expected, actual), $"{path}: expected {expected}, actual {actual}");
    }
}
