namespace Akusiap.Domain.Access;

public class Plan
{
    public int PlanId { get; set; }

    public PlanCode Code { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IncludesAllExams { get; set; }

    public bool IncludesAuthoring { get; set; }

    public bool IncludesClasses { get; set; }

    /// <summary>50 for Free; a fair-use cap for paid plans.</summary>
    public int MaxOwnQuestions { get; set; }

    /// <summary>1 for Free.</summary>
    public int MaxOwnExams { get; set; }

    public int MaxImportRows { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PlanPrice> Prices { get; } = [];
}
