using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;
using saas.Modules.Suppliers.DTOs;
using saas.Modules.Suppliers.Entities;

namespace saas.Modules.Suppliers.Services;

public interface ISupplierService
{
    Task<List<SupplierListItemDto>> GetListAsync(string? search = null);
    Task<SupplierFormDto> CreateEmptyAsync();
    Task<SupplierFormDto?> GetFormAsync(Guid id);
    Task CreateAsync(SupplierFormDto dto);
    Task UpdateAsync(Guid id, SupplierFormDto dto);
    Task DeleteAsync(Guid id);
    Task<SupplierDetailsDto?> GetDetailsAsync(Guid id);

    Task<List<SupplierContactListItemDto>> GetContactsAsync(Guid supplierId);
    Task<SupplierContactFormDto> CreateEmptyContactAsync(Guid supplierId);
    Task<SupplierContactFormDto?> GetContactFormAsync(Guid contactId);
    Task CreateContactAsync(SupplierContactFormDto dto);
    Task UpdateContactAsync(Guid contactId, SupplierContactFormDto dto);
    Task DeleteContactAsync(Guid contactId);
}

public class SupplierService : ISupplierService
{
    private readonly TenantDbContext _db;

    public SupplierService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<SupplierListItemDto>> GetListAsync(string? search = null)
    {
        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term)
                || (s.ContactName != null && s.ContactName.ToLower().Contains(term))
                || (s.ContactEmail != null && s.ContactEmail.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierListItemDto
            {
                Id = s.Id,
                Name = s.Name,
                ContactName = s.ContactName,
                ContactEmail = s.ContactEmail,
                ContactPhone = s.ContactPhone,
                Rating = s.Rating,
                IsActive = s.IsActive
            })
            .ToListAsync();
    }

    public async Task<SupplierFormDto> CreateEmptyAsync()
    {
        return new SupplierFormDto
        {
            IsActive = true,
            CurrencyOptions = await GetCurrencyCodesAsync()
        };
    }

    public async Task<SupplierFormDto?> GetFormAsync(Guid id)
    {
        var entity = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return null;

        return new SupplierFormDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ContactName = entity.ContactName,
            ContactEmail = entity.ContactEmail,
            ContactPhone = entity.ContactPhone,
            Notes = entity.Notes,
            IsActive = entity.IsActive,
            RegistrationNumber = entity.RegistrationNumber,
            BankDetails = entity.BankDetails,
            PaymentTerms = entity.PaymentTerms,
            DefaultCommissionPercentage = entity.DefaultCommissionPercentage,
            DefaultCurrencyCode = entity.DefaultCurrencyCode,
            Rating = entity.Rating,
            Website = entity.Website,
            Address = entity.Address,
            CurrencyOptions = await GetCurrencyCodesAsync()
        };
    }

    public async Task CreateAsync(SupplierFormDto dto)
    {
        _db.Suppliers.Add(new Supplier
        {
            Name = dto.Name.Trim(),
            ContactName = Normalize(dto.ContactName),
            ContactEmail = Normalize(dto.ContactEmail),
            ContactPhone = Normalize(dto.ContactPhone),
            Notes = Normalize(dto.Notes),
            IsActive = dto.IsActive,
            RegistrationNumber = Normalize(dto.RegistrationNumber),
            BankDetails = Normalize(dto.BankDetails),
            PaymentTerms = Normalize(dto.PaymentTerms),
            DefaultCommissionPercentage = dto.DefaultCommissionPercentage,
            DefaultCurrencyCode = Normalize(dto.DefaultCurrencyCode),
            Rating = dto.Rating,
            Website = Normalize(dto.Website),
            Address = Normalize(dto.Address)
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Guid id, SupplierFormDto dto)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException($"Supplier {id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.ContactName = Normalize(dto.ContactName);
        entity.ContactEmail = Normalize(dto.ContactEmail);
        entity.ContactPhone = Normalize(dto.ContactPhone);
        entity.Notes = Normalize(dto.Notes);
        entity.IsActive = dto.IsActive;
        entity.RegistrationNumber = Normalize(dto.RegistrationNumber);
        entity.BankDetails = Normalize(dto.BankDetails);
        entity.PaymentTerms = Normalize(dto.PaymentTerms);
        entity.DefaultCommissionPercentage = dto.DefaultCommissionPercentage;
        entity.DefaultCurrencyCode = Normalize(dto.DefaultCurrencyCode);
        entity.Rating = dto.Rating;
        entity.Website = Normalize(dto.Website);
        entity.Address = Normalize(dto.Address);

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException($"Supplier {id} was not found.");

        // Remove associated contacts
        var contacts = await _db.SupplierContacts.Where(c => c.SupplierId == id).ToListAsync();
        _db.SupplierContacts.RemoveRange(contacts);

        _db.Suppliers.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<SupplierDetailsDto?> GetDetailsAsync(Guid id)
    {
        var entity = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return null;

        var contacts = await _db.SupplierContacts.AsNoTracking()
            .Where(c => c.SupplierId == id)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Name)
            .Select(c => new SupplierContactListItemDto
            {
                Id = c.Id,
                SupplierId = c.SupplierId,
                Name = c.Name,
                Role = c.Role,
                Email = c.Email,
                Phone = c.Phone,
                IsPrimary = c.IsPrimary
            })
            .ToListAsync();

        var bookingItemCount = await _db.BookingItems
            .CountAsync(bi => bi.SupplierId == id);

        return new SupplierDetailsDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ContactName = entity.ContactName,
            ContactEmail = entity.ContactEmail,
            ContactPhone = entity.ContactPhone,
            Notes = entity.Notes,
            IsActive = entity.IsActive,
            RegistrationNumber = entity.RegistrationNumber,
            BankDetails = entity.BankDetails,
            PaymentTerms = entity.PaymentTerms,
            DefaultCommissionPercentage = entity.DefaultCommissionPercentage,
            DefaultCurrencyCode = entity.DefaultCurrencyCode,
            Rating = entity.Rating,
            Website = entity.Website,
            Address = entity.Address,
            CreatedAt = entity.CreatedAt,
            Contacts = contacts,
            BookingItemCount = bookingItemCount
        };
    }

    // ========== CONTACTS ==========

    public Task<SupplierContactFormDto> CreateEmptyContactAsync(Guid supplierId)
        => Task.FromResult(new SupplierContactFormDto { SupplierId = supplierId });

    public async Task<SupplierContactFormDto?> GetContactFormAsync(Guid contactId)
    {
        return await _db.SupplierContacts.AsNoTracking()
            .Where(c => c.Id == contactId)
            .Select(c => new SupplierContactFormDto
            {
                Id = c.Id,
                SupplierId = c.SupplierId,
                Name = c.Name,
                Role = c.Role,
                Email = c.Email,
                Phone = c.Phone,
                IsPrimary = c.IsPrimary
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<SupplierContactListItemDto>> GetContactsAsync(Guid supplierId)
    {
        return await _db.SupplierContacts.AsNoTracking()
            .Where(c => c.SupplierId == supplierId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Name)
            .Select(c => new SupplierContactListItemDto
            {
                Id = c.Id,
                SupplierId = c.SupplierId,
                Name = c.Name,
                Role = c.Role,
                Email = c.Email,
                Phone = c.Phone,
                IsPrimary = c.IsPrimary
            })
            .ToListAsync();
    }

    public async Task CreateContactAsync(SupplierContactFormDto dto)
    {
        _db.SupplierContacts.Add(new SupplierContact
        {
            SupplierId = dto.SupplierId,
            Name = dto.Name.Trim(),
            Role = Normalize(dto.Role),
            Email = Normalize(dto.Email),
            Phone = Normalize(dto.Phone),
            IsPrimary = dto.IsPrimary
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateContactAsync(Guid contactId, SupplierContactFormDto dto)
    {
        var entity = await _db.SupplierContacts.FirstOrDefaultAsync(c => c.Id == contactId)
            ?? throw new InvalidOperationException($"Supplier contact {contactId} was not found.");

        entity.Name = dto.Name.Trim();
        entity.Role = Normalize(dto.Role);
        entity.Email = Normalize(dto.Email);
        entity.Phone = Normalize(dto.Phone);
        entity.IsPrimary = dto.IsPrimary;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteContactAsync(Guid contactId)
    {
        var entity = await _db.SupplierContacts.FirstOrDefaultAsync(c => c.Id == contactId)
            ?? throw new InvalidOperationException($"Supplier contact {contactId} was not found.");
        _db.SupplierContacts.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // ========== HELPERS ==========

    private async Task<List<string>> GetCurrencyCodesAsync()
        => await _db.Currencies.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => c.Code)
            .ToListAsync();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
