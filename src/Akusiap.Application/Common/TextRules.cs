namespace Akusiap.Application.Common;

/// <summary>Shared checks for names and descriptions typed by admins.</summary>
public static class TextRules
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;

    /// <summary>"{0} wajib diisi." with the field label as {0}.</summary>
    public const string Required = "Validation.Required";

    /// <summary>"{0} maksimal {1} karakter." with the field label and the limit.</summary>
    public const string TooLong = "Validation.StringLength";

    /// <summary>Trims; returns null for blank text.</summary>
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static void RequireName(List<Problem> errors, string field, string fieldLabelKey, string? name)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (Clean(name) is not { } value)
        {
            errors.Add(new Problem(Required, field, fieldLabelKey));
        }
        else if (value.Length > NameMaxLength)
        {
            errors.Add(new Problem(TooLong, field, fieldLabelKey, NameMaxLength));
        }
    }

    public static void LimitDescription(List<Problem> errors, string field, string fieldLabelKey, string? description)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (Clean(description) is { Length: > DescriptionMaxLength })
        {
            errors.Add(new Problem(TooLong, field, fieldLabelKey, DescriptionMaxLength));
        }
    }
}
