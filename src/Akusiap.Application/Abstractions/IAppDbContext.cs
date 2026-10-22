using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Moderation;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Abstractions;

/// <summary>The database as seen by handlers. Implemented by <c>AppDbContext</c> in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>> UserRoles { get; }

    DbSet<Subject> Subjects { get; }

    DbSet<Topic> Topics { get; }

    DbSet<Question> Questions { get; }

    DbSet<QuestionOption> QuestionOptions { get; }

    DbSet<Exam> Exams { get; }

    DbSet<ExamQuestion> ExamQuestions { get; }

    DbSet<RulePreset> RulePresets { get; }

    DbSet<Plan> Plans { get; }

    DbSet<PlanPrice> PlanPrices { get; }

    DbSet<Subscription> Subscriptions { get; }

    DbSet<ExamPurchase> ExamPurchases { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Payment> Payments { get; }

    DbSet<Refund> Refunds { get; }

    DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; }

    DbSet<PromoCode> PromoCodes { get; }

    DbSet<SavedPaymentMethod> SavedPaymentMethods { get; }

    DbSet<Attempt> Attempts { get; }

    DbSet<AttemptItem> AttemptItems { get; }

    DbSet<AttemptItemOption> AttemptItemOptions { get; }

    DbSet<ReviewCard> ReviewCards { get; }

    DbSet<ReviewLog> ReviewLogs { get; }

    DbSet<ContentReport> ContentReports { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<Domain.Classes.Classroom> Classrooms { get; }

    DbSet<Domain.Classes.ClassMember> ClassMembers { get; }

    DbSet<Domain.Classes.ClassAssignment> ClassAssignments { get; }

    DbSet<Domain.Gamification.UserBadge> UserBadges { get; }

    DbSet<Domain.Gamification.LeaderboardSettings> LeaderboardSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>For use cases that need several statements to succeed or fail together.</summary>
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
