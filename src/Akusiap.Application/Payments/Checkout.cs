using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Akusiap.Application.Payments;

/// <summary>What is being bought: an exam (by id) or a plan package (by plan price id).</summary>
public sealed record CheckoutItemRef(long? ExamId, long? PlanPriceId)
{
    public bool IsValid => (ExamId is null) != (PlanPriceId is null);
}

/// <param name="AccessEndsAt">When access would end if paid now (D-03, D-04): extends running access, else starts now.</param>
/// <param name="IsExtension">The learner already has running access that this purchase extends.</param>
/// <param name="PendingOrderNumber">A payment already waiting for this item.</param>
public sealed record CheckoutSummary(
    CheckoutItemRef Item,
    string Title,
    long PriceIdr,
    int? AccessDays,
    int? DurationMonths,
    bool IncludesAllExams,
    DateTime AccessEndsAt,
    bool IsExtension,
    string? PendingOrderNumber);

/// <summary>
/// Checkout (PAY-03, stories P-01, P-02, P-03, D-58). <see cref="SummaryAsync"/> describes the purchase for the signed-in
/// user; <see cref="PlaceOrderAsync"/> checks the email is confirmed, the terms are accepted and the channel is offered,
/// then reuses or creates the item row (ExamPurchase or Subscription, AwaitingPayment when new) and opens one Pending
/// payment through <see cref="CreatePayment"/>. Fulfilment on payment is PAY-06.
/// </summary>
public sealed class Checkout(IAppDbContext db, ICurrentUser currentUser, IClock clock, IOptions<PaymentChannelOptions> channels)
{
    public const string EmailNotConfirmed = "Checkout.EmailNotConfirmed";
    public const string TermsRequired = "Checkout.TermsRequired";
    public const string ChannelRequired = "Checkout.ChannelRequired";

    public IReadOnlyList<PaymentChannelSetting> ChannelsFor(long amountIdr) => channels.Value.AvailableFor(amountIdr);

    public async Task<CheckoutSummary?> SummaryAsync(CheckoutItemRef item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var userId = UserId();
        var now = clock.UtcNow;
        if (!item.IsValid)
        {
            return null;
        }

        if (item.ExamId is { } examId)
        {
            var exam = await db.Exams
                .Where(e => e.Id == examId && e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published && !e.IsFree && e.PriceIdr != null && e.AccessDays != null)
                .Select(e => new { e.Title, Price = e.PriceIdr!.Value, Days = e.AccessDays!.Value })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (exam is null)
            {
                return null;
            }

            var purchase = await LivePurchaseAsync(userId, examId, cancellationToken).ConfigureAwait(false);
            var running = purchase is { Status: ExamPurchaseStatus.Active } && purchase.AccessExpiresAt > now ? purchase.AccessExpiresAt : null;
            var pending = purchase is null ? null : await PendingOrderAsync(p => p.ExamPurchaseId == purchase.Id, cancellationToken).ConfigureAwait(false);
            return new CheckoutSummary(item, exam.Title, exam.Price, exam.Days, null, false, (running ?? now).AddDays(exam.Days), running is not null, pending);
        }

        var package = await db.PlanPrices
            .Where(p => p.Id == item.PlanPriceId && p.IsActive && p.Plan!.IsActive && p.Plan.Code != PlanCode.Free)
            .Select(p => new { p.PlanId, p.Plan!.Name, p.Plan.IncludesAllExams, p.PriceIdr, p.DurationMonths })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return null;
        }

        var subscription = await LiveSubscriptionAsync(userId, package.PlanId, cancellationToken).ConfigureAwait(false);
        var runningEnd = subscription is { Status: SubscriptionStatus.Active } && subscription.CurrentPeriodEnd > now ? subscription.CurrentPeriodEnd : null;
        var pendingOrder = subscription is null ? null : await PendingOrderAsync(p => p.SubscriptionId == subscription.Id, cancellationToken).ConfigureAwait(false);
        return new CheckoutSummary(
            item, package.Name, package.PriceIdr, null, package.DurationMonths, package.IncludesAllExams,
            (runningEnd ?? now).AddMonths(package.DurationMonths), runningEnd is not null, pendingOrder);
    }

    public async Task<bool> IsEmailConfirmedAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens (or finds) the Pending payment and returns its order number.</summary>
    public async Task<Result<string>> PlaceOrderAsync(CheckoutItemRef item, string? channelCode, bool acceptedTerms, CreatePayment createPayment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createPayment);
        var summary = await SummaryAsync(item, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            return Result.Fail<string>(Problem.NotFound);
        }

        if (summary.PendingOrderNumber is { } existing)
        {
            return Result.Ok(existing);
        }

        var problems = new List<Problem>();
        if (!await IsEmailConfirmedAsync(cancellationToken).ConfigureAwait(false))
        {
            problems.Add(new Problem(EmailNotConfirmed));
        }

        var channel = ChannelsFor(summary.PriceIdr).FirstOrDefault(c => c.Code == channelCode);
        if (channel is null)
        {
            problems.Add(new Problem(ChannelRequired, "Channel"));
        }

        if (!acceptedTerms)
        {
            problems.Add(new Problem(TermsRequired, "AcceptTerms"));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<string>(problems);
        }

        var userId = UserId();
        PaymentItem paymentItem;
        if (item.ExamId is { } examId)
        {
            var purchase = await LivePurchaseAsync(userId, examId, cancellationToken).ConfigureAwait(false);
            if (purchase is null)
            {
                purchase = new ExamPurchase { UserId = userId, ExamId = examId, PricePaidIdr = summary.PriceIdr, Status = ExamPurchaseStatus.AwaitingPayment };
                db.ExamPurchases.Add(purchase);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            paymentItem = PaymentItem.ForExamPurchase(purchase.Id, summary.Title);
        }
        else
        {
            var package = await db.PlanPrices.SingleAsync(p => p.Id == item.PlanPriceId, cancellationToken).ConfigureAwait(false);
            var subscription = await LiveSubscriptionAsync(userId, package.PlanId, cancellationToken).ConfigureAwait(false);
            if (subscription is null)
            {
                subscription = new Subscription { UserId = userId, PlanId = package.PlanId, PlanPriceId = package.Id, CreatedAt = clock.UtcNow };
                db.Subscriptions.Add(subscription);
            }
            else
            {
                // The row remembers the package being bought, so fulfilment (PAY-06) knows how many months to add.
                subscription.PlanPriceId = package.Id;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            paymentItem = PaymentItem.ForSubscription(subscription.Id, $"{summary.Title} {summary.DurationMonths} bulan");
        }

        var payment = await createPayment.HandleAsync(
            new CreatePaymentRequest(userId, paymentItem, summary.PriceIdr, channel!.Method, channel.Channel), cancellationToken).ConfigureAwait(false);
        return payment.Succeeded ? Result.Ok(payment.Value!.OrderNumber) : Result.Fail<string>(payment.Problems);
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Checkout needs a signed-in user.");

    private Task<ExamPurchase?> LivePurchaseAsync(Guid userId, long examId, CancellationToken cancellationToken) =>
        db.ExamPurchases.SingleOrDefaultAsync(
            p => p.UserId == userId && p.ExamId == examId && p.Status != ExamPurchaseStatus.Abandoned && p.Status != ExamPurchaseStatus.Refunded,
            cancellationToken);

    private Task<Subscription?> LiveSubscriptionAsync(Guid userId, long planId, CancellationToken cancellationToken) =>
        db.Subscriptions.SingleOrDefaultAsync(
            s => s.UserId == userId && s.PlanId == planId && s.Status != SubscriptionStatus.Abandoned && s.Status != SubscriptionStatus.Refunded,
            cancellationToken);

    private Task<string?> PendingOrderAsync(System.Linq.Expressions.Expression<Func<Payment, bool>> forItem, CancellationToken cancellationToken) =>
        db.Payments.Where(p => p.Status == PaymentStatus.Pending).Where(forItem).Select(p => p.OrderNumber).FirstOrDefaultAsync(cancellationToken);
}
