using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace saas.Shared;

public static class Breadcrumbs
{
    public const string ParentLabelKey = "BreadcrumbParentLabel";
    public const string ParentUrlKey = "BreadcrumbParentUrl";
    public const string CurrentKey = "BreadcrumbCurrent";

    public static void Set(ViewDataDictionary viewData, string current, string? parentLabel = null, string? parentUrl = null)
    {
        viewData[CurrentKey] = current;

        if (!string.IsNullOrWhiteSpace(parentLabel))
        {
            viewData[ParentLabelKey] = parentLabel;
        }

        if (!string.IsNullOrWhiteSpace(parentUrl))
        {
            viewData[ParentUrlKey] = parentUrl;
        }
    }
}