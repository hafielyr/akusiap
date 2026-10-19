using Akusiap.Application;
using Akusiap.Infrastructure;
using Akusiap.Web.Health;
using Akusiap.Web.Identity;
using Akusiap.Web.Localization;
using Akusiap.Web.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<IdentityErrorDescriber, LocalizedIdentityErrorDescriber>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Akusiap.Application.Abstractions.ICurrentUser, HttpCurrentUser>();
var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();
authentication.AddGoogleIfConfigured(builder.Configuration);
builder.Services.AddScoped<GoogleAccounts>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = AccountRoutes.SignIn;
    options.LogoutPath = AccountRoutes.SignOut;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    // A signed-in user without the right role gets a real 403 and the Indonesian status page, not a redirect.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAppAuthorization();
builder.Services.AddAppLocalization();
builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeAreaFolder(AuthorizationPolicies.AdminArea, "/", AuthorizationPolicies.AdminOnly);
        options.Conventions.AuthorizeAreaFolder(AuthorizationPolicies.LearnArea, "/", AuthorizationPolicies.SignedIn);
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
builder.Services.Configure<SupportOptions>(builder.Configuration.GetSection(SupportOptions.SectionName));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));
builder.Services.AddScoped<AccountEmails>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Empty 4xx/5xx responses (403, 404 …) are re-run through /status/{code}; placed before localization so the page
// is shown in the visitor's language.
app.UseStatusCodePagesWithReExecute("/status/{0}");
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
// Unknown addresses get 404 for every method. Without this, the static-asset catch-all (GET/HEAD only) turns a POST to
// an unknown address into 405. The empty 404 is then shown as the friendly status page.
app.MapFallback(context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

app.Run();

// Exposes Program to WebApplicationFactory in the test projects.
public partial class Program;
