namespace Akusiap.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Sender address, for example <c>Akusiap &lt;no-reply@akusiap.id&gt;</c>.</summary>
    public string From { get; set; } = "Akusiap <no-reply@akusiap.local>";

    /// <summary>Folder for the development file sender, relative to the app's content root.</summary>
    public string PickupDirectory { get; set; } = ".dev-mail";
}
