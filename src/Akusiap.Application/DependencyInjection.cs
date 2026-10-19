using Akusiap.Application.Questions;
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
        return services;
    }
}
