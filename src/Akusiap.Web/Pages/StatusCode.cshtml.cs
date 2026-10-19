using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages;

/// <summary>
/// Friendly page for 4xx/5xx responses without a body (403, 404 …), re-executed by UseStatusCodePagesWithReExecute.
/// Re-execution keeps the original method, so POSTs land here too (hence no antiforgery check).
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class StatusCodeModel : PageModel
{
    public int Code { get; private set; }

    public void OnGet(int code) => Show(code);

    public void OnPost(int code) => Show(code);

    private void Show(int code)
    {
        // Opened directly rather than re-executed: behave like a missing page.
        if (HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is null)
        {
            code = StatusCodes.Status404NotFound;
            Response.StatusCode = code;
        }

        Code = code;
    }
}
