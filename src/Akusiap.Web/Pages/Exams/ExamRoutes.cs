using System.Globalization;

namespace Akusiap.Web.Pages.Exams;

/// <summary>
/// Exam URLs (Indonesian slugs, D-31). Start, checkout and plans are built by later cards (SES-02, PAY-03, CAT-03); the
/// exam page links to them already (D-43).
/// </summary>
public static class ExamRoutes
{
    public const string Catalog = "/ujian";

    /// <summary>The plans page (CAT-03).</summary>
    public const string Plans = "/harga";

    public static string Detail(long examId) => $"/ujian/{Id(examId)}";

    /// <summary>The session setup page (SES-02), for full access and the trial alike: the attempt's scope comes from the entitlement.</summary>
    public static string Start(long examId) => $"/ujian/{Id(examId)}/mulai";

    /// <summary>Single-exam checkout (PAY-03).</summary>
    public static string Checkout(long examId) => $"/checkout?exam={Id(examId)}";

    private static string Id(long examId) => examId.ToString(CultureInfo.InvariantCulture);
}
