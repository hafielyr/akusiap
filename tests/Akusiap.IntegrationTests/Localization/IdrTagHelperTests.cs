using Akusiap.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Akusiap.IntegrationTests.Localization;

public sealed class IdrTagHelperTests
{
    [Fact]
    public void Renders_rupiah_text_without_a_wrapping_tag()
    {
        var context = new TagHelperContext([], new Dictionary<object, object>(), "id");
        var output = new TagHelperOutput("idr", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        new IdrTagHelper { Value = 49000 }.Process(context, output);

        Assert.Null(output.TagName);
        Assert.Equal("Rp49.000", output.Content.GetContent());
    }
}
