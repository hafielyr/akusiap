using Microsoft.Playwright;

namespace Akusiap.E2E;

/// <summary>
/// A phone-sized browser context. When <c>PLAYWRIGHT_TRACES_DIR</c> is set (CI sets it), a Playwright trace
/// (screenshots, DOM snapshots, network) is recorded and saved there on dispose, so failed runs can be replayed with
/// <c>playwright.ps1 show-trace &lt;file&gt;</c>.
/// </summary>
public sealed class Phone(IBrowserContext context, string? tracePath) : IAsyncDisposable
{
    public IBrowserContext Context => context;

    public Task<IPage> NewPageAsync() => context.NewPageAsync();

    public async ValueTask DisposeAsync()
    {
        if (tracePath is not null)
        {
            await context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
        }

        await context.DisposeAsync();
    }
}
