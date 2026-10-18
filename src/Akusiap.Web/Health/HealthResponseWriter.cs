using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akusiap.Web.Health;

/// <summary>Writes <c>{"status":"Healthy","checks":{"database":"Healthy"}}</c> so each dependency is visible.</summary>
internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body), context.RequestAborted);
    }
}
