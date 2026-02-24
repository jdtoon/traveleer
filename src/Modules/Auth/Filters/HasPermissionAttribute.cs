using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using saas.Shared;

namespace saas.Modules.Auth.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class HasPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _permissions;

    public HasPermissionAttribute(params string[] permissions)
    {
        _permissions = permissions;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.HasAnyPermission(_permissions))
        {
            var isHtmx = context.HttpContext.Request.Headers.ContainsKey("HX-Request");
            if (isHtmx)
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    Content = "<div class=\"alert alert-error\"><span>You don't have permission to access this resource.</span></div>",
                    ContentType = "text/html"
                };
            }
            else
            {
                context.Result = new ForbidResult(AuthSchemes.Tenant);
            }
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
