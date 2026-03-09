using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Inventory.DTOs;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.Inventory.Services;

public interface IInventoryService
{
    Task<PaginatedList<InventoryListItemDto>> GetListAsync(string? type = null, string? search = null, int page = 1, int pageSize = 12);
    Task<InventoryDto> CreateEmptyAsync();
    Task<InventoryDto?> GetAsync(Guid id);
    Task CreateAsync(InventoryDto dto);
    Task UpdateAsync(Guid id, InventoryDto dto);
    Task DeleteAsync(Guid id);
}

public class InventoryService : IInventoryService
{
    private readonly TenantDbContext _db;

    public InventoryService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<PaginatedList<InventoryListItemDto>> GetListAsync(string? type = null, string? search = null, int page = 1, int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.InventoryItems
            .AsNoTracking()
            .Include(x => x.Destination)
            .Include(x => x.Supplier)
            .AsQueryable();

        if (TryParseType(type, out var kind))
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                (x.Description != null && x.Description.ToLower().Contains(term)) ||
                (x.Address != null && x.Address.ToLower().Contains(term)) ||
                (x.Destination != null && x.Destination.Name.ToLower().Contains(term)) ||
                (x.Supplier != null && x.Supplier.Name.ToLower().Contains(term)));
        }

        var projected = query
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name)
            .Select(x => new InventoryListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                Kind = x.Kind,
                Description = x.Description,
                BaseCost = x.BaseCost,
                ImageUrl = x.ImageUrl,
                Address = x.Address,
                Rating = x.Rating,
                DestinationId = x.DestinationId,
                DestinationName = x.Destination != null ? x.Destination.Name : null,
                SupplierId = x.SupplierId,
                SupplierName = x.Supplier != null ? x.Supplier.Name : null
            });

        return await PaginatedList<InventoryListItemDto>.CreateAsync(projected, page, pageSize);
    }

    public async Task<InventoryDto> CreateEmptyAsync()
        => new()
        {
            DestinationOptions = await GetDestinationOptionsAsync(),
            SupplierOptions = await GetSupplierOptionsAsync()
        };

    public async Task<InventoryDto?> GetAsync(Guid id)
    {
        var dto = await _db.InventoryItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new InventoryDto
            {
                Id = x.Id,
                Name = x.Name,
                Kind = x.Kind,
                Description = x.Description,
                BaseCost = x.BaseCost,
                ImageUrl = x.ImageUrl,
                Address = x.Address,
                Rating = x.Rating,
                DestinationId = x.DestinationId,
                SupplierId = x.SupplierId
            })
            .FirstOrDefaultAsync();

        if (dto is null)
        {
            return null;
        }

        dto.DestinationOptions = await GetDestinationOptionsAsync();
        dto.SupplierOptions = await GetSupplierOptionsAsync();
        return dto;
    }

    public async Task CreateAsync(InventoryDto dto)
    {
        var entity = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Kind = dto.Kind,
            Description = Normalize(dto.Description),
            BaseCost = dto.BaseCost,
            ImageUrl = Normalize(dto.ImageUrl),
            Address = Normalize(dto.Address),
            Rating = NormalizeRating(dto.Kind, dto.Rating),
            DestinationId = dto.DestinationId,
            SupplierId = dto.SupplierId
        };

        _db.InventoryItems.Add(entity);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Guid id, InventoryDto dto)
    {
        var entity = await _db.InventoryItems.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Inventory item {id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.Kind = dto.Kind;
        entity.Description = Normalize(dto.Description);
        entity.BaseCost = dto.BaseCost;
        entity.ImageUrl = Normalize(dto.ImageUrl);
        entity.Address = Normalize(dto.Address);
        entity.Rating = NormalizeRating(dto.Kind, dto.Rating);
        entity.DestinationId = dto.DestinationId;
        entity.SupplierId = dto.SupplierId;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.InventoryItems.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Inventory item {id} was not found.");

        _db.InventoryItems.Remove(entity);
        await _db.SaveChangesAsync();
    }

    private async Task<List<InventoryOptionDto>> GetDestinationOptionsAsync()
        => await _db.Destinations
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new InventoryOptionDto { Id = x.Id, Label = x.Name })
            .ToListAsync();

    private async Task<List<InventoryOptionDto>> GetSupplierOptionsAsync()
        => await _db.Suppliers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new InventoryOptionDto { Id = x.Id, Label = x.Name })
            .ToListAsync();

    private static bool TryParseType(string? type, out InventoryItemKind kind)
        => Enum.TryParse(type, true, out kind);

    private static int? NormalizeRating(InventoryItemKind kind, int? rating)
        => kind == InventoryItemKind.Hotel ? rating : null;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
