using Akusiap.Domain;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Akusiap.Web.TagHelpers;

/// <summary>Renders a Rupiah amount: <c>&lt;idr value="49000" /&gt;</c> → <c>Rp49.000</c>.</summary>
[HtmlTargetElement("idr", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IdrTagHelper : TagHelper
{
    public long Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = null;
        output.Content.SetContent(Money.FormatIdr(Value));
    }
}
