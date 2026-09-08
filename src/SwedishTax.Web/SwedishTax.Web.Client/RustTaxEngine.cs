using Microsoft.JSInterop;
using SwedishTax.Core;
using SwedishTax.Native;

namespace SwedishTax.Web.Client;

public interface IRustTaxEngine
{
    ValueTask<EngineResponse> CalculateAsync(byte table, TaxAgeGroup ageGroup, IncomePlan plan);
    ValueTask<string?> LoadWorkspaceAsync();
    ValueTask SaveWorkspaceAsync(string workspace);
}

public sealed class RustTaxEngine(IJSRuntime js) : IRustTaxEngine
{
    private const string StorageKey = "swedish-tax.workspace";

    // P/Invoke runs synchronously in the same WebAssembly runtime and memory.
    public ValueTask<EngineResponse> CalculateAsync(byte table, TaxAgeGroup ageGroup, IncomePlan plan) =>
        ValueTask.FromResult(NativeCalculator.Calculate(table, ageGroup, plan));
    public ValueTask<string?> LoadWorkspaceAsync() =>
        js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
    public ValueTask SaveWorkspaceAsync(string workspace) =>
        js.InvokeVoidAsync("localStorage.setItem", StorageKey, workspace);
}
