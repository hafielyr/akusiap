using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Abstractions;

/// <summary>The database as seen by handlers. Implemented by <c>AppDbContext</c> in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
