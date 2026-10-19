using Akusiap.Application.Questions;
using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Akusiap.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Questions;

/// <summary>Question list with filters (subject, topic, status, type, text) and paging (ADM-T2).</summary>
public sealed class IndexModel(ListQuestions listQuestions, ListSubjects listSubjects, ListTopicOptions listTopics) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "subjek")]
    public long? SubjectId { get; set; }

    [BindProperty(SupportsGet = true, Name = "topik")]
    public long? TopicId { get; set; }

    [BindProperty(SupportsGet = true)]
    public QuestionStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "jenis")]
    public QuestionType? Type { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "hal")]
    public int PageNumber { get; set; } = 1;

    public QuestionPage Results { get; private set; } = new([], 1, ListQuestions.PageSize, 0);

    public IReadOnlyList<SubjectOption> Subjects { get; private set; } = [];

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    public bool IsFiltered => SubjectId is not null || TopicId is not null || Status is not null || Type is not null || !string.IsNullOrWhiteSpace(Search);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Subjects = await listSubjects.OptionsAsync(cancellationToken);
        Topics = await listTopics.HandleAsync(cancellationToken);
        Results = await listQuestions.HandleAsync(new QuestionFilter(SubjectId, TopicId, Status, Type, Search, PageNumber), cancellationToken);
    }

    /// <summary>The list URL for another page, keeping the current filters.</summary>
    public string PageUrl(int page)
    {
        var query = new List<KeyValuePair<string, string?>>();
        void Add(string key, object? value)
        {
            if (value is not null && value.ToString() is { Length: > 0 } text)
            {
                query.Add(new(key, text));
            }
        }

        Add("subjek", SubjectId);
        Add("topik", TopicId);
        Add("status", Status);
        Add("jenis", Type);
        Add("q", Search);
        Add("hal", page);
        return Microsoft.AspNetCore.Http.QueryString.Create(query).ToUriComponent() is { Length: > 0 } qs ? $"/admin/soal{qs}" : "/admin/soal";
    }
}
