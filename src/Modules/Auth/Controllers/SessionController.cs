using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("{slug}/profile/sessions")]
[Authorize(Policy = "TenantUser")]
public class SessionController : SwapController
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUser _currentUser;

    public SessionController(TenantDbContext tenantDb, ICurrentUser currentUser)
    {
        _tenantDb = tenantDb;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromRoute] string slug)
    {
        var sessions = await _tenantDb.Set<UserSession>()
            .Where(s => s.UserId == _currentUser.UserId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync();

        ViewData["Title"] = "Active Sessions";
        return SwapView("Sessions", sessions);
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke([FromRoute] string slug, [FromRoute] Guid id)
    {
        var session = await _tenantDb.Set<UserSession>()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == _currentUser.UserId);

        if (session is not null)
        {
            session.IsRevoked = true;
            await _tenantDb.SaveChangesAsync();
        }

        return await Index(slug);
    }

    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll([FromRoute] string slug)
    {
        var sessions = await _tenantDb.Set<UserSession>()
            .Where(s => s.UserId == _currentUser.UserId && !s.IsRevoked)
            .ToListAsync();

        foreach (var session in sessions)
            session.IsRevoked = true;

        await _tenantDb.SaveChangesAsync();

        ViewData["Success"] = "All sessions have been revoked. You may need to log in again.";
        return await Index(slug);
    }
}
