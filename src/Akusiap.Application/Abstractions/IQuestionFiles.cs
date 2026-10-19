using Akusiap.Application.Questions.Import;

namespace Akusiap.Application.Abstractions;

/// <summary>Reads question rows from an uploaded xlsx or csv file (ADM-T7). Implemented in Infrastructure.</summary>
public interface IQuestionFileReader
{
    public const string UnsupportedFile = "Admin.Import.UnsupportedFile";
    public const string UnreadableFile = "Admin.Import.UnreadableFile";

    /// <summary>Arg: the missing column names, comma separated.</summary>
    public const string MissingColumns = "Admin.Import.MissingColumns";

    /// <summary>Reads at most <paramref name="maxRows"/> + 1 data rows, so the caller can tell the file is too long.</summary>
    QuestionFileContent Read(Stream stream, string fileName, int maxRows);
}

/// <summary>The downloadable xlsx template.</summary>
public interface IQuestionTemplate
{
    byte[] Create();
}
