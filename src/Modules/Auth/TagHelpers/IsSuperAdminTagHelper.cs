using Microsoft.AspNetCore.Razor.TagHelpers;
using saas.Shared;

namespace saas.Modules.Auth.TagHelpers;

[HtmlTargetElement("is-super-admin")]
public class IsSuperAdminTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    public IsSuperAdminTagHelper(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (!_currentUser.IsSuperAdmin)
            output.SuppressOutput();
    }
}
