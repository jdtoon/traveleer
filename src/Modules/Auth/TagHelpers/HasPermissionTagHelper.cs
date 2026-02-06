using Microsoft.AspNetCore.Razor.TagHelpers;
using saas.Shared;

namespace saas.Modules.Auth.TagHelpers;

[HtmlTargetElement("has-permission", Attributes = "name")]
public class HasPermissionTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    public HasPermissionTagHelper(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    [HtmlAttributeName("name")]
    public string Permission { get; set; } = string.Empty;

    [HtmlAttributeName("any")]
    public string? AnyPermissions { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        bool hasAccess;

        if (!string.IsNullOrEmpty(AnyPermissions))
        {
            var permissions = AnyPermissions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            hasAccess = _currentUser.HasAnyPermission(permissions);
        }
        else
        {
            hasAccess = _currentUser.HasPermission(Permission);
        }

        if (!hasAccess)
            output.SuppressOutput();
    }
}
