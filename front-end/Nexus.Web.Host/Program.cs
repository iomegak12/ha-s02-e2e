using Nexus.Web.Host.Auth;
using Nexus.Web.Host.Proxy;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ─────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console());

    // ── Services ─────────────────────────────────────────────────────────────
    builder.Services.AddControllersWithViews();
    builder.Services.AddRazorPages();

    // Session cookie authentication (BFF pattern)
    builder.Services.AddAuthentication("NexusCookie")
        .AddCookie("NexusCookie", options =>
        {
            var sessionCfg = builder.Configuration.GetSection("Session");
            options.Cookie.Name = sessionCfg["CookieName"] ?? "nexus.sid";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.SlidingExpiration = sessionCfg.GetValue<bool>("SlidingExpiration");
            options.ExpireTimeSpan = TimeSpan.FromMinutes(
                sessionCfg.GetValue<int>("ExpiryMinutes", 60));
            options.LoginPath = "/api/v1/session/login";
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

    builder.Services.AddAuthorization();

    // Token store — holds JWT keyed by session ID (never sent to browser)
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<ITokenStore, InMemoryTokenStore>();

    // Named HttpClient for each downstream service
    var servicesCfg = builder.Configuration.GetSection("Services");
    builder.Services.AddHttpClient("Identity",
        c => c.BaseAddress = new Uri(servicesCfg["Identity"] ?? "http://localhost:5001"));
    builder.Services.AddHttpClient("Patients",
        c => c.BaseAddress = new Uri(servicesCfg["Patients"] ?? "http://localhost:5002"));
    builder.Services.AddHttpClient("Doctors",
        c => c.BaseAddress = new Uri(servicesCfg["Doctors"] ?? "http://localhost:5003"));
    builder.Services.AddHttpClient("Branches",
        c => c.BaseAddress = new Uri(servicesCfg["Branches"] ?? "http://localhost:5004"));
    builder.Services.AddHttpClient("Audit",
        c => c.BaseAddress = new Uri(servicesCfg["Audit"] ?? "http://localhost:5005"));

    // ── App ───────────────────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    // BFF reverse proxy — must come before MapFallbackToFile
    app.UseMiddleware<ReverseProxyMiddleware>();

    app.MapControllers();
    app.MapRazorPages();
    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Nexus.Web.Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
