using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

#if STATIC_SITE
builder.RootComponents.Add<SwedishTax.Web.Client.Routes>("#app");
builder.RootComponents.Add<Microsoft.AspNetCore.Components.Web.HeadOutlet>("head::after");
#endif

builder.Services.AddScoped<SwedishTax.Web.Client.IRustTaxEngine, SwedishTax.Web.Client.RustTaxEngine>();

await builder.Build().RunAsync();
