using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Clients.DTOs;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Clients.Services;

public interface IClientService
{
    Task<PaginatedList<ClientListItemDto>> GetListAsync(string? search = null, int page = 1, int pageSize = 12);
    Task<ClientDto> CreateEmptyAsync();
    Task<ClientDto?> GetAsync(Guid id);
    Task<ClientDetailsDto?> GetDetailsAsync(Guid id);
    Task CreateAsync(ClientDto dto);
    Task UpdateAsync(Guid id, ClientDto dto);
    Task DeleteAsync(Guid id);
    Task<bool> EmailExistsAsync(string email, Guid? excludeId = null);
}

public class ClientService : IClientService
{
    private readonly TenantDbContext _db;

    public ClientService(TenantDbContext db)
    {
        _db = db;
    }

    public Task<ClientDto> CreateEmptyAsync()
        => Task.FromResult(new ClientDto());

    public async Task<PaginatedList<ClientListItemDto>> GetListAsync(string? search = null, int page = 1, int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Clients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Company != null && c.Company.ToLower().Contains(term)) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.ToLower().Contains(term)) ||
                (c.Country != null && c.Country.ToLower().Contains(term)));
        }

        var projected = query
            .OrderBy(c => c.Name)
            .Select(c => new ClientListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                Company = c.Company,
                Email = c.Email,
                Phone = c.Phone,
                Country = c.Country,
                CreatedAt = c.CreatedAt
            });

        return await PaginatedList<ClientListItemDto>.CreateAsync(projected, page, pageSize);
    }

    public async Task<ClientDto?> GetAsync(Guid id)
    {
        return await _db.Clients
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClientDto
            {
                Id = c.Id,
                Name = c.Name,
                Company = c.Company,
                Email = c.Email,
                Phone = c.Phone,
                Address = c.Address,
                Country = c.Country,
                Notes = c.Notes
            })
            .FirstOrDefaultAsync();
    }

    public async Task<ClientDetailsDto?> GetDetailsAsync(Guid id)
    {
        return await _db.Clients
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClientDetailsDto
            {
                Id = c.Id,
                Name = c.Name,
                Company = c.Company,
                Email = c.Email,
                Phone = c.Phone,
                Address = c.Address,
                Country = c.Country,
                Notes = c.Notes,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task CreateAsync(ClientDto dto)
    {
        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Company = Normalize(dto.Company),
            Email = Normalize(dto.Email),
            Phone = Normalize(dto.Phone),
            Address = Normalize(dto.Address),
            Country = Normalize(dto.Country),
            Notes = Normalize(dto.Notes)
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Guid id, ClientDto dto)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException($"Client {id} was not found.");

        client.Name = dto.Name.Trim();
        client.Company = Normalize(dto.Company);
        client.Email = Normalize(dto.Email);
        client.Phone = Normalize(dto.Phone);
        client.Address = Normalize(dto.Address);
        client.Country = Normalize(dto.Country);
        client.Notes = Normalize(dto.Notes);

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException($"Client {id} was not found.");

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> EmailExistsAsync(string email, Guid? excludeId = null)
    {
        var normalizedEmail = email.Trim().ToLower();
        return await _db.Clients.AnyAsync(c =>
            c.Email != null &&
            c.Email.ToLower() == normalizedEmail &&
            (!excludeId.HasValue || c.Id != excludeId.Value));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}