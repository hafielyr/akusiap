using Akusiap.Application.Users;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Users;

/// <summary><c>/admin/pengguna</c> (USR-01): search by email or name, filter by role, active flag and subscription; 20 per page.</summary>
public sealed class IndexModel(ListUsers listUsers) : PageModel
{
    public const string Route = "/admin/pengguna";

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "peran")]
    public string? Role { get; set; }

    [BindProperty(SupportsGet = true, Name = "aktif")]
    public bool? IsActive { get; set; }

    [BindProperty(SupportsGet = true, Name = "langganan")]
    public SubscriptionFilter Subscription { get; set; } = SubscriptionFilter.Any;

    [BindProperty(SupportsGet = true, Name = "hal")]
    public int PageNumber { get; set; } = 1;

    public UserPage Results { get; private set; } = new([], 1, ListUsers.PageSize, 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var role = Role is not null && Akusiap.Domain.Users.RoleNames.All.Contains(Role) ? Role : null;
        Results = await listUsers.HandleAsync(new UserFilter(Search, role, IsActive, Subscription, PageNumber), cancellationToken);
    }

    /// <summary>The list URL for another page, keeping every filter.</summary>
    public string PageUrl(int page)
    {
        var query = new QueryBuilder();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query.Add("q", Search.Trim());
        }

        if (!string.IsNullOrEmpty(Role))
        {
            query.Add("peran", Role);
        }

        if (IsActive is { } active)
        {
            query.Add("aktif", active ? "true" : "false");
        }

        if (Subscription != SubscriptionFilter.Any)
        {
            query.Add("langganan", Subscription.ToString());
        }

        if (page > 1)
        {
            query.Add("hal", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Route + query.ToQueryString();
    }
}
