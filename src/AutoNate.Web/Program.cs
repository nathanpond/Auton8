using AutoNate.Web.Components;
using AutoNate.Web.Configuration;

#if DEBUG
// Rider's plain ".NET Project" launcher runs the built executable directly, which skips
// launchSettings.json. Mirror the local development defaults unless the user already
// supplied explicit values.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:5108");
}
#endif

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

if (builder.Environment.IsDevelopment())
{
    builder.Configuration["ReloadStaticAssetsAtRuntime"] = bool.FalseString;
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddOptions<FlowableOptions>()
    .BindConfiguration(FlowableOptions.SectionName);
builder.Services.AddOptions<DaprOptions>()
    .BindConfiguration(DaprOptions.SectionName);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
