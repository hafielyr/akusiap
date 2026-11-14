namespace Akusiap.Domain.Content;

/// <summary>A question's difficulty measured from learners' answers (DIF-01). Separate from the author's 1–5 rating.</summary>
public enum MeasuredDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// The difficulty score (DIF-01, D-140). A question gets a label once it has at least <see cref="MinAnswers"/> right or
/// wrong answers. Score = (wrong + ½ × marked) / (right + wrong), at most 1: a marked answer shows doubt, so it counts as
/// half a miss. Score under 0.30 is Easy, 0.60 and up is Hard, in between Medium.
/// </summary>
public static class QuestionDifficulty
{
    public const int MinAnswers = 10;
    public const decimal MarkedWeight = 0.5m;
    public const decimal MediumFrom = 0.30m;
    public const decimal HardFrom = 0.60m;

    public static decimal Score(int correct, int wrong, int marked)
    {
        var answers = correct + wrong;
        return answers <= 0 ? 0m : Math.Min(1m, (wrong + (MarkedWeight * marked)) / answers);
    }

    public static MeasuredDifficulty? Of(int correct, int wrong, int marked)
    {
        if (correct + wrong < MinAnswers)
        {
            return null;
        }

        var score = Score(correct, wrong, marked);
        return score >= HardFrom ? MeasuredDifficulty.Hard : score >= MediumFrom ? MeasuredDifficulty.Medium : MeasuredDifficulty.Easy;
    }
}
