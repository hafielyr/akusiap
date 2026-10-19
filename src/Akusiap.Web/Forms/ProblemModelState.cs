using Akusiap.Application.Common;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Forms;

/// <summary>Shows use-case problems as form errors in the user's language.</summary>
public static class ProblemModelState
{
    /// <summary>
    /// Adds each problem under <c>{prefix}.{Field}</c> (or as a page-level error when it has no field). String arguments
    /// that are resource keys, such as field labels, are translated; other values are shown as they are.
    /// </summary>
    public static void AddProblems(
        this ModelStateDictionary modelState, IEnumerable<Problem> problems, IStringLocalizer<SharedResource> t, string prefix)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(t);
        foreach (var problem in problems)
        {
            var key = problem.Field is null ? string.Empty : $"{prefix}.{problem.Field}";
            modelState.AddModelError(key, Describe(problem, t));
        }
    }

    public static string Describe(Problem problem, IStringLocalizer<SharedResource> t)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(t);
        var args = problem.Args.Select(a => a is string s ? (object)t[s].Value : a).ToArray();
        return t[problem.Code, args];
    }
}
