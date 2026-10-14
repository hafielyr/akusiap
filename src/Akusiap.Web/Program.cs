using Akusiap.Web.Localization;
using Akusiap.Web.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppLocalization();
builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
builder.Services.AddHealthChecks();
builder.Services.Configure<SupportOptions>(builder.Configuration.GetSection(SupportOptions.SectionName));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Exposes Program to WebApplicationFactory in the test projects.
public partial class Program;
