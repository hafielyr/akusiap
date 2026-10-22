using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Catalog;
using Akusiap.Application.Exams;
using Akusiap.Application.Payments;
using Akusiap.Application.Plans;
using Akusiap.Application.Questions;
using Akusiap.Application.Questions.Import;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Sessions;
using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Akusiap.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.Application;

/// <summary>Registers the use-case handlers (plain classes, no MediatR; architecture.md).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // One instance per request: the viewer context is cached in it (ENT-02).
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<ExamQuestionScope>();
        services.AddScoped<GetUserAccess>();
        services.AddScoped<ManageAccess>();
        services.AddScoped<GetMyAccess>();
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddScoped<StartAttempt>();
        services.AddScoped<GetSessionSetup>();
        services.AddScoped<GetPlayer>();
        services.AddScoped<SaveAnswer>();
        services.AddScoped<AttemptFinisher>();
        services.AddScoped<SubmitAttempt>();
        services.AddScoped<PracticeHelp>();
        services.AddScoped<GetResult>();
        services.AddScoped<GetReview>();
        services.AddScoped<ListHistory>();
        services.AddScoped<ClaimGuestAttempts>();
        services.AddScoped<Gamification.Leaderboards>();
        services.AddScoped<Gamification.Badges>();
        services.AddScoped<Gamification.DailyDrills>();
        services.AddScoped<Gamification.LeaderboardAdmin>();
        services.AddScoped<Gamification.WeeklyLeaderboardAwards>();
        services.AddScoped<Access.AccessibleQuestions>();
        services.AddScoped<Classes.Classes>();
        services.AddScoped<ExpireAccess>();
        services.AddScoped<ExpirePendingPayments>();
        services.AddScoped<ListDueReminders>();
        services.AddScoped<OwnExams.OwnExams>();
        services.AddScoped<Moderation.QuestionReports>();
        services.AddScoped<Memorization.Flashcards>();
        services.AddScoped<OwnExams.MixedPractice>();
        services.AddScoped<Sharing.ShareExam>();
        services.AddScoped<Moderation.ExamReports>();
        services.AddScoped<ExpireAttempts>();
        services.AddScoped<FindOpenAttempt>();
        services.AddScoped<CreatePayment>();
        services.AddScoped<Checkout>();
        services.AddScoped<GetPayments>();
        services.AddScoped<AdminPayments>();
        services.AddScoped<ListPlanPricing>();
        services.AddScoped<SavePlanPrices>();
        services.AddScoped<ListPlanLimits>();
        services.AddScoped<SavePlanLimits>();
        services.AddScoped<ListUsers>();
        services.AddScoped<GetUserDetail>();
        services.AddScoped<ApplyGatewayNotification>();
        services.AddScoped<ListCatalog>();
        services.AddScoped<GetCatalogExam>();
        services.AddScoped<GetPricing>();
        services.AddScoped<ListSubjects>();
        services.AddScoped<GetSubject>();
        services.AddScoped<SaveSubject>();
        services.AddScoped<DeleteSubject>();
        services.AddScoped<GetTopic>();
        services.AddScoped<SaveTopic>();
        services.AddScoped<DeleteTopic>();
        services.AddScoped<ListTopicOptions>();
        services.AddScoped<ListQuestions>();
        services.AddScoped<GetQuestion>();
        services.AddScoped<SaveQuestion>();
        services.AddScoped<GetAllowance>();
        services.AddScoped<OwnQuestions.OwnQuestions>();
        services.AddMemoryCache();
        services.AddScoped<ImportQuestions>();
        services.AddScoped<ListExams>();
        services.AddScoped<GetExam>();
        services.AddScoped<SaveExam>();
        services.AddScoped<DeleteExam>();
        services.AddScoped<GetExamQuestions>();
        services.AddScoped<SearchQuestionsForExam>();
        services.AddScoped<ChangeExamQuestions>();
        services.AddScoped<PublishExam>();
        services.AddScoped<ListRulePresetOptions>();
        services.AddScoped<ListRulePresets>();
        services.AddScoped<GetRulePreset>();
        services.AddScoped<SaveRulePreset>();
        services.AddScoped<DeleteRulePreset>();
        return services;
    }
}
