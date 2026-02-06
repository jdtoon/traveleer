using Microsoft.AspNetCore.Razor.TagHelpers;
using saas.Shared;

namespace saas.Modules.Auth.TagHelpers;

[HtmlTargetElement("is-authenticated")]
public class IsAuthenticatedTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    public IsAuthenticatedTagHelper(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (!_currentUser.IsAuthenticated)
            output.SuppressOutput();
    }
}
