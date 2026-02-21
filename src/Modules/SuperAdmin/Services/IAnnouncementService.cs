using saas.Modules.SuperAdmin.Entities;

namespace saas.Modules.SuperAdmin.Services;

public interface IAnnouncementService
{
    Task<List<Announcement>> GetActiveAnnouncementsAsync();
    Task<List<Announcement>> GetAllAnnouncementsAsync();
    Task DeactivateAsync(Guid id);
}
