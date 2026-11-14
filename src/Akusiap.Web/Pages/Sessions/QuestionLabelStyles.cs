using Akusiap.Domain.Content;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>Chip colours for the measured difficulty (DIF-01): green, amber, red.</summary>
public static class QuestionLabelStyles
{
    public static string Tone(MeasuredDifficulty level) => level switch
    {
        MeasuredDifficulty.Easy => "bg-green-100 text-green-800",
        MeasuredDifficulty.Medium => "bg-amber-100 text-amber-900",
        _ => "bg-red-100 text-red-800",
    };
}
