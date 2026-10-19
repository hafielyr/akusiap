using Akusiap.Application.Exams;
using Akusiap.Application.Questions;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.Application;

/// <summary>Registers the use-case handlers (plain classes, no MediatR; architecture.md).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
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
        services.AddScoped<ListExams>();
        services.AddScoped<GetExam>();
        services.AddScoped<SaveExam>();
        services.AddScoped<DeleteExam>();
        services.AddScoped<GetExamQuestions>();
        services.AddScoped<SearchQuestionsForExam>();
        services.AddScoped<ChangeExamQuestions>();
        services.AddScoped<ListRulePresetOptions>();
        services.AddScoped<ListRulePresets>();
        services.AddScoped<GetRulePreset>();
        services.AddScoped<SaveRulePreset>();
        services.AddScoped<DeleteRulePreset>();
        return services;
    }
}
