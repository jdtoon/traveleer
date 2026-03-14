using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Communications.DTOs;
using saas.Modules.Communications.Entities;
using saas.Shared;

namespace saas.Modules.Communications.Services;

public interface ICommunicationService
{
    Task<CommunicationListDto> GetByClientAsync(Guid clientId);
    Task<CommunicationListDto> GetByBookingAsync(Guid bookingId);
    Task<CommunicationListDto> GetBySupplierAsync(Guid supplierId);
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

    public async Task<CommunicationListDto> GetByClientAsync(Guid clientId)
    {
        var entries = await QueryEntries()
            .Where(e => e.ClientId == clientId)
            .ToListAsync();

        var dtos = await MapToDtosAsync(entries);
        return new CommunicationListDto { Entries = dtos, ClientId = clientId };
    }

    public async Task<CommunicationListDto> GetByBookingAsync(Guid bookingId)
    {
        var entries = await QueryEntries()
            .Where(e => e.BookingId == bookingId)
            .ToListAsync();

        var dtos = await MapToDtosAsync(entries);
        return new CommunicationListDto { Entries = dtos, BookingId = bookingId };
    }

    public async Task<CommunicationListDto> GetBySupplierAsync(Guid supplierId)
    {
        var entries = await QueryEntries()
            .Where(e => e.SupplierId == supplierId)
            .ToListAsync();

        var dtos = await MapToDtosAsync(entries);
        return new CommunicationListDto { Entries = dtos, SupplierId = supplierId };
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
        return _db.CommunicationEntries.OrderByDescending(e => e.OccurredAt);
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
