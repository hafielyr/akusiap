using System.Globalization;
using System.Text;
using Akusiap.Application.Payments;
using Akusiap.Domain;
using Akusiap.Domain.Payments;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Transactions;

/// <summary>
/// <c>/admin/transaksi</c> (PAY-10): payments filtered by status, method, Jakarta date range and user (email, name or order
/// number), 25 per page, newest first. <c>?handler=Csv</c> exports exactly the filtered rows.
/// </summary>
public sealed class IndexModel(AdminPayments payments) : PageModel
{
    public const string Route = "/admin/transaksi";

    [BindProperty(SupportsGet = true)]
    public PaymentStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "metode")]
    public PaymentMethod? Method { get; set; }

    [BindProperty(SupportsGet = true, Name = "dari")]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "sampai")]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Who { get; set; }

    [BindProperty(SupportsGet = true, Name = "hal")]
    public int PageNumber { get; set; } = 1;

    public AdminPaymentPage Results { get; private set; } = new([], 1, AdminPayments.PageSize, 0);

    private AdminPaymentFilter Filter => new(Status, Method, From, To, Who, PageNumber);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Results = await payments.ListAsync(Filter, cancellationToken);

    public async Task<FileContentResult> OnGetCsvAsync(CancellationToken cancellationToken)
    {
        var rows = await payments.ExportAsync(Filter, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("order_number,created_at_wib,paid_at_wib,user_email,user_name,item,amount_idr,method,channel,status,late_payment,failure_reason,retry_count");
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(',',
                Cell(r.OrderNumber),
                Cell(Wib(r.CreatedAt)),
                Cell(r.PaidAt is { } paid ? Wib(paid) : string.Empty),
                Cell(r.UserEmail),
                Cell(r.UserName),
                Cell(r.Item),
                r.AmountIdr.ToString(CultureInfo.InvariantCulture),
                Cell(r.Method.ToString()),
                Cell(r.Channel ?? string.Empty),
                Cell(r.Status.ToString()),
                r.IsLatePayment ? "true" : "false",
                Cell(r.FailureReason ?? string.Empty),
                r.RetryCount.ToString(CultureInfo.InvariantCulture)));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"transaksi-{JakartaTime.Today(DateTime.UtcNow):yyyyMMdd}.csv");
    }

    /// <summary>This page with the current filters and another page number or handler.</summary>
    public string LinkTo(int? page = null, string? handler = null)
    {
        var query = new QueryBuilder();
        if (handler is not null)
        {
            query.Add("handler", handler);
        }

        if (Status is { } status)
        {
            query.Add("Status", status.ToString());
        }

        if (Method is { } method)
        {
            query.Add("metode", method.ToString());
        }

        if (From is { } from)
        {
            query.Add("dari", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (To is { } to)
        {
            query.Add("sampai", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(Who))
        {
            query.Add("q", Who.Trim());
        }

        if (page is > 1)
        {
            query.Add("hal", page.Value.ToString(CultureInfo.InvariantCulture));
        }

        return Route + query.ToQueryString();
    }

    private static string Wib(DateTime utc) => JakartaTime.ToLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>RFC 4180 quoting; a leading = + - @ is prefixed with a quote so spreadsheets don't run it as a formula.</summary>
    private static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
