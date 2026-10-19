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
        services.AddScoped<CreatePayment>();
        services.AddScoped<ListPlanPricing>();
        services.AddScoped<SavePlanPrices>();
        services.AddScoped<ListUsers>();
        services.AddScoped<GetUserDetail>();
        services.AddScoped<ApplyGatewayNotification>();
        services.AddScoped<ListCatalog>();
        services.AddScoped<GetCatalogExam>();
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
