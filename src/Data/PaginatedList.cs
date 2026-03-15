using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace saas.Data;

public record PaginationModel(int PageIndex, int TotalPages, int TotalCount, string ListUrl, string HxTarget, int PageSize = 12, bool SupportsPageSizeSelection = false)
{
    public string WithPage(int pageIndex) => BuildUrl(pageIndex, SupportsPageSizeSelection ? PageSize : null);

    public string WithPageSize(int pageSize) => BuildUrl(1, pageSize);

    private string BuildUrl(int pageIndex, int? pageSize)
    {
        var queryIndex = ListUrl.IndexOf('?');
        var path = queryIndex >= 0 ? ListUrl[..queryIndex] : ListUrl;
        var query = queryIndex >= 0
            ? QueryHelpers.ParseQuery(ListUrl[(queryIndex + 1)..]).ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        query["page"] = pageIndex.ToString();

        if (pageSize.HasValue)
            query["pageSize"] = pageSize.Value.ToString();
        else
            query.Remove("pageSize");

        var builder = new QueryBuilder(query.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
        return path + builder.ToQueryString().Value;
    }
}

public class PaginatedList<T>
{
    public List<T> Items { get; }
    public int PageIndex { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }
    public int PageSize { get; }

    public PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
    {
        PageIndex = pageIndex;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        TotalCount = count;
        PageSize = pageSize;
        Items = items;
    }

    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    public PaginationModel ToPagination(string listUrl, string hxTarget, bool supportsPageSizeSelection = false)
        => new(PageIndex, TotalPages, TotalCount, listUrl, hxTarget, PageSize, supportsPageSizeSelection);

    public static async Task<PaginatedList<T>> CreateAsync(IQueryable<T> source, int pageIndex, int pageSize)
    {
        var count = await source.CountAsync();
        var items = await source.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PaginatedList<T>(items, count, pageIndex, pageSize);
    }
}
