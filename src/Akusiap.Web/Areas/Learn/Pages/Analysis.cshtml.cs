using Akusiap.Application.Planning;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary>
/// <c>/learn/analisis</c> (ANL-01, D-137): for each exam the learner practised or set a goal for, the SKD subtests and
/// topics weakest first, from every answer they gave, and a learning plan built from them. Guests who took a trial land
/// here after registering (GST-02).
/// </summary>
public sealed class AnalysisModel(LearningAnalysis analysis) : PageModel
{
    public const string Route = "/learn/analisis";

    public IReadOnlyList<ExamAnalysis> Exams { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Exams = await analysis.AllAsync(cancellationToken);
}
