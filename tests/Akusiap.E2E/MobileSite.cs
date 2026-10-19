using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.Playwright;

namespace Akusiap.E2E;

/// <summary>
/// The app on a real Kestrel port with the per-run database, plus one headless Chromium for the run.
/// Each test gets its own browser context at 360×640 (an iPhone SE-like phone), so cookies are never shared.
/// </summary>
public sealed class MobileSite : IAsyncLifetime, IAsyncDisposable
{
    public const int Width = 360;
    public const int Height = 640;

    private readonly PostgresFixture _database = new();
    private AppFactory? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri BaseAddress { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new AppFactory(_database);
        _app.UseKestrel(0);
        _app.StartServer();
        BaseAddress = _app.ClientOptions.BaseAddress;
        await _app.SeedReferenceDataAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    /// <summary>A fresh phone-sized browser context and page; dispose the context when done.</summary>
    public async Task<IBrowserContext> NewPhoneAsync()
    {
        var browser = _browser ?? throw new InvalidOperationException("The site is not started.");
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = Width, Height = Height },
            DeviceScaleFactor = 2,
            IsMobile = true,
            HasTouch = true,
            UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Mobile Safari/537.36",
            Locale = "id-ID",
            TimezoneId = "Asia/Jakarta",
        });
    }
}

[CollectionDefinition(Name)]
public sealed class UsesMobileSite : ICollectionFixture<MobileSite>
{
    public const string Name = "Mobile site";
}
