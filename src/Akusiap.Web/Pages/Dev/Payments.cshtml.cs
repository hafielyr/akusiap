using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Payments;
using Akusiap.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Web.Pages.Dev;

/// <summary>
/// <c>/dev/pembayaran</c>, Development only (PAY-01): start a test payment for a paid exam with the fake gateway, see its
/// instructions, and play the gateway's "paid" notification. Answers 404 in every other environment.
/// Unlocking the exam after payment comes with PAY-06.
/// </summary>
[Authorize]
public sealed class PaymentsModel(
    IAppDbContext db,
    CreatePayment createPayment,
    ApplyGatewayNotification applyNotification,
    ICurrentUser currentUser,
    IClock clock,
    IWebHostEnvironment environment) : PageModel
{
    public const string Route = "/dev/pembayaran";

    public sealed record ExamOption(long Id, string Title, long PriceIdr);

    public IReadOnlyList<ExamOption> Exams { get; private set; } = [];

    public IReadOnlyList<Payment> Payments { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!environment.IsDevelopment())
        {
            context.Result = NotFound();
        }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        Exams = await db.Exams
            .Where(e => e.OwnerKind == OwnerKind.App && !e.IsFree && e.PriceIdr != null && e.Status == ExamStatus.Published)
            .OrderBy(e => e.Title)
            .Select(e => new ExamOption(e.Id, e.Title, e.PriceIdr!.Value))
            .ToListAsync(cancellationToken);
        Payments = await db.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(10)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(long examId, PaymentMethod method, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == examId && !e.IsFree && e.PriceIdr != null, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        var purchase = await db.ExamPurchases.SingleOrDefaultAsync(
            p => p.UserId == userId && p.ExamId == examId && p.Status != ExamPurchaseStatus.Abandoned && p.Status != ExamPurchaseStatus.Refunded,
            cancellationToken);
        if (purchase is null)
        {
            purchase = new ExamPurchase { UserId = userId, ExamId = examId, PricePaidIdr = exam.PriceIdr!.Value, Status = ExamPurchaseStatus.AwaitingPayment };
            db.ExamPurchases.Add(purchase);
            await db.SaveChangesAsync(cancellationToken);
        }

        var result = await createPayment.HandleAsync(
            new CreatePaymentRequest(userId, PaymentItem.ForExamPurchase(purchase.Id, exam.Title), exam.PriceIdr!.Value, method, method == PaymentMethod.VirtualAccount ? "BCA" : null),
            cancellationToken);
        Message = result.Succeeded ? $"Created {result.Value!.OrderNumber}" : string.Join(", ", result.Problems.Select(p => p.Code));
        return Redirect(Route);
    }

    public async Task<IActionResult> OnPostNotifyAsync(long paymentId, string status, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId && p.UserId == userId, cancellationToken);
        if (payment is null || status is not ("PAID" or "EXPIRED" or "FAILED"))
        {
            return NotFound();
        }

        var (outcome, _) = await applyNotification.HandleAsync(
            FakePaymentGateway.CreateNotification(payment.InvoiceNumber, status, payment.AmountIdr), cancellationToken);
        Message = $"{payment.OrderNumber}: {status} → {outcome} at {clock.UtcNow:u}";
        return Redirect(Route);
    }
}
