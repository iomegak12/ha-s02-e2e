using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Web.Client;
using Nexus.Web.Client.Services.Auth;
using Nexus.Web.Client.Services.Branches;
using Nexus.Web.Client.Services.Patients;
using Nexus.Web.Client.Services.ServiceHealth;
using Nexus.Web.Client.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Single named HttpClient pointing to the Host (BFF)
// All API calls go through /api/v1/* on the same origin — no CORS needed
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// Auth state
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<NexusAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<NexusAuthStateProvider>());
builder.Services.AddScoped<SessionState>();

// Domain services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IServiceHealthService, ServiceHealthService>();

await builder.Build().RunAsync();
