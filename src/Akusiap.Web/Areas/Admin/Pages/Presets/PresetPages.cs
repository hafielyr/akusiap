using Akusiap.Application.RulePresets;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Presets;

public sealed class IndexModel(ListRulePresets listPresets) : PageModel
{
    public IReadOnlyList<RulePresetDetail> Presets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => Presets = await listPresets.HandleAsync(cancellationToken);
}

public sealed class CreateModel(SaveRulePreset savePreset, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public PresetInput Input { get; set; } = PresetInput.Blank();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var (request, numberProblems) = Input.ToRequest(null);
        var result = numberProblems.Count == 0 ? await savePreset.HandleAsync(request, cancellationToken) : null;
        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Saved";
            return Redirect($"/admin/aturan/{result.Value}");
        }

        ModelState.AddProblems(result?.Problems ?? numberProblems, t, nameof(Input));
        return Page();
    }
}

/// <summary>Edit a system preset, or delete one that is not the default and not used by an exam.</summary>
public sealed class EditModel(
    GetRulePreset getPreset,
    SaveRulePreset savePreset,
    DeleteRulePreset deletePreset,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public PresetInput Input { get; set; } = new();

    public RulePresetDetail Preset { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = PresetInput.From(Preset);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var (request, numberProblems) = Input.ToRequest(Id);
        var result = numberProblems.Count == 0 ? await savePreset.HandleAsync(request, cancellationToken) : null;
        if (result is { IsNotFound: true })
        {
            return NotFound();
        }

        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Saved";
            return Redirect($"/admin/aturan/{Id}");
        }

        ModelState.AddProblems(result?.Problems ?? numberProblems, t, nameof(Input));
        return await LoadAsync(cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await deletePreset.HandleAsync(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            if (!await LoadAsync(cancellationToken))
            {
                return NotFound();
            }

            Input = PresetInput.From(Preset);
            return Page();
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect("/admin/aturan");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var preset = await getPreset.HandleAsync(Id, cancellationToken);
        if (preset is null)
        {
            return false;
        }

        Preset = preset;
        return true;
    }
}
