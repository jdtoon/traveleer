using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Communications.DTOs;
using saas.Modules.Communications.Entities;
using saas.Shared;

namespace saas.Modules.Communications.Services;

public interface ICommunicationService
{
    Task<CommunicationListDto> GetByClientAsync(Guid clientId, int page = 1, int pageSize = 20);
    Task<CommunicationListDto> GetByBookingAsync(Guid bookingId, int page = 1, int pageSize = 20);
    Task<CommunicationListDto> GetBySupplierAsync(Guid supplierId, int page = 1, int pageSize = 20);
    Task<CommunicationEntryDto?> GetByIdAsync(Guid id);
    Task<CommunicationEntry> CreateAsync(CreateCommunicationDto dto);
    Task UpdateAsync(Guid id, UpdateCommunicationDto dto);
    Task DeleteAsync(Guid id);
    Task AutoLogEmailAsync(Guid? clientId, Guid? supplierId, Guid? bookingId, string subject, string recipient);
}

public class CommunicationService : ICommunicationService
{
    private readonly TenantDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CommunicationService(TenantDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CommunicationListDto> GetByClientAsync(Guid clientId, int page = 1, int pageSize = 20)
    {
        return await GetListAsync(e => e.ClientId == clientId, list => list with { ClientId = clientId }, page, pageSize);
    }

    public async Task<CommunicationListDto> GetByBookingAsync(Guid bookingId, int page = 1, int pageSize = 20)
    {
        return await GetListAsync(e => e.BookingId == bookingId, list => list with { BookingId = bookingId }, page, pageSize);
    }

    public async Task<CommunicationListDto> GetBySupplierAsync(Guid supplierId, int page = 1, int pageSize = 20)
    {
        return await GetListAsync(e => e.SupplierId == supplierId, list => list with { SupplierId = supplierId }, page, pageSize);
    }

    public async Task<CommunicationEntryDto?> GetByIdAsync(Guid id)
    {
        var entry = await _db.CommunicationEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null) return null;
        var dtos = await MapToDtosAsync([entry]);
        return dtos.FirstOrDefault();
    }

    public async Task<CommunicationEntry> CreateAsync(CreateCommunicationDto dto)
    {
        var entry = new CommunicationEntry
        {
            ClientId = dto.ClientId,
            SupplierId = dto.SupplierId,
            BookingId = dto.BookingId,
            Channel = dto.Channel,
            Direction = dto.Direction,
            Subject = dto.Subject,
            Content = dto.Content,
            OccurredAt = dto.OccurredAt ?? DateTime.UtcNow,
            LoggedByUserId = _currentUser.UserId ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.CommunicationEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(Guid id, UpdateCommunicationDto dto)
    {
        var entry = await _db.CommunicationEntries.FindAsync(id);
        if (entry is null) return;

        entry.Channel = dto.Channel;
        entry.Direction = dto.Direction;
        entry.Subject = dto.Subject;
        entry.Content = dto.Content;
        if (dto.OccurredAt.HasValue)
            entry.OccurredAt = dto.OccurredAt.Value;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entry = await _db.CommunicationEntries.FindAsync(id);
        if (entry is null) return;

        _db.CommunicationEntries.Remove(entry);
        await _db.SaveChangesAsync();
    }

    public async Task AutoLogEmailAsync(Guid? clientId, Guid? supplierId, Guid? bookingId, string subject, string recipient)
    {
        var entry = new CommunicationEntry
        {
            ClientId = clientId,
            SupplierId = supplierId,
            BookingId = bookingId,
            Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound,
            Subject = subject,
            Content = $"Email sent to {recipient}: {subject}",
            OccurredAt = DateTime.UtcNow,
            LoggedByUserId = _currentUser.UserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.CommunicationEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    private IQueryable<CommunicationEntry> QueryEntries()
    {
        return _db.CommunicationEntries.AsNoTracking().OrderByDescending(e => e.OccurredAt);
    }

    private async Task<CommunicationListDto> GetListAsync(
        System.Linq.Expressions.Expression<Func<CommunicationEntry, bool>> predicate,
        Func<CommunicationListDto, CommunicationListDto> assignContext,
        int page,
        int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 10, 50);
        var filtered = QueryEntries().Where(predicate);
        var totalCount = await filtered.CountAsync();
        var entries = await filtered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var dtos = await MapToDtosAsync(entries);
        var list = new CommunicationListDto
        {
            Entries = dtos,
            PageIndex = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize)
        };

        return assignContext(list);
    }

    private async Task<IReadOnlyList<CommunicationEntryDto>> MapToDtosAsync(List<CommunicationEntry> entries)
    {
        if (entries.Count == 0) return [];

        var userIds = entries.Select(e => e.LoggedByUserId).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email ?? u.Id);

        return entries.Select(e => new CommunicationEntryDto
        {
            Id = e.Id,
            ClientId = e.ClientId,
            SupplierId = e.SupplierId,
            BookingId = e.BookingId,
            Channel = e.Channel,
            Direction = e.Direction,
            Subject = e.Subject,
            Content = e.Content,
            OccurredAt = e.OccurredAt,
            LoggedByUserId = e.LoggedByUserId,
            LoggedByName = userNames.GetValueOrDefault(e.LoggedByUserId, e.LoggedByUserId),
            CreatedAt = e.CreatedAt
        }).ToList();
    }
}
