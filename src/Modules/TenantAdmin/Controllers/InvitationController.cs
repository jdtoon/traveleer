using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.Auth;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Filters;
using saas.Modules.Notifications.Services;
using saas.Modules.TenantAdmin.Entities;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
[Route("{slug}/admin/invitation")]
public class InvitationController : SwapController
{
    private readonly TenantDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _templateService;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications;

    public InvitationController(
        TenantDbContext db,
        UserManager<AppUser> userManager,
        IEmailService emailService,
        IEmailTemplateService templateService,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
        _templateService = templateService;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _notifications = notifications;
    }

    [HttpGet("")]
    [HasPermission(TenantAdminPermissions.UsersRead)]
    public async Task<IActionResult> Index()
    {
        var invitations = await _db.Set<TeamInvitation>()
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return SwapView(SwapViews.Invitation.PendingInvitations, invitations);
    }

    [HttpPost("send")]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> Send([FromForm] string email, [FromForm] string? roleId)
    {
        if (string.IsNullOrWhiteSpace(email))
            return SwapResponse().WithErrorToast("Email is required").Build();

        // Check if already invited
        var existing = await _db.Set<TeamInvitation>()
            .AnyAsync(i => i.Email == email && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow);

        if (existing)
            return SwapResponse().WithErrorToast("An invitation is already pending for this email").Build();

        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
            return SwapResponse().WithErrorToast("A user with this email already exists").Build();

        string? roleName = null;
        if (!string.IsNullOrEmpty(roleId))
        {
            var role = await _db.Roles.FindAsync(roleId);
            roleName = role?.Name;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var invitation = new TeamInvitation
        {
            Id = Guid.NewGuid(),
            Email = email.Trim(),
            RoleId = roleId,
            RoleName = roleName,
            Token = token,
            InvitedByUserId = _currentUser.UserId ?? string.Empty,
            InvitedByEmail = _currentUser.Email,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _db.Set<TeamInvitation>().Add(invitation);
        await _db.SaveChangesAsync();

        // Send invitation email using TeamInvitation template
        var slug = _tenantContext.Slug;
        var acceptUrl = Url.Action("Accept", "Invitation", new { slug, token }, Request.Scheme) ?? $"/{slug}/admin/invitation/accept?token={Uri.EscapeDataString(token)}";
        await SendInvitationEmailAsync(email, acceptUrl, roleName ?? "Member", invitation.ExpiresAt);

        return SwapResponse()
            .WithSuccessToast("Invitation sent!")
            .WithView(SwapViews.TenantAdmin._ModalClose)
            .Build();
    }

    [HttpPost("revoke/{id}")]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var invitation = await _db.Set<TeamInvitation>().FindAsync(id);
        if (invitation is null) return NotFound();

        invitation.Status = InvitationStatus.Revoked;
        await _db.SaveChangesAsync();

        return SwapResponse()
            .WithWarningToast("Invitation revoked")
            .Build();
    }

    [HttpPost("resend/{id}")]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> Resend(Guid id)
    {
        var invitation = await _db.Set<TeamInvitation>().FindAsync(id);
        if (invitation is null) return NotFound();

        invitation.Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(7);
        invitation.Status = InvitationStatus.Pending;
        await _db.SaveChangesAsync();

        var slug = _tenantContext.Slug;
        var acceptUrl = Url.Action("Accept", "Invitation", new { slug, token = invitation.Token }, Request.Scheme) ?? $"/{slug}/admin/invitation/accept?token={Uri.EscapeDataString(invitation.Token)}";
        await SendInvitationEmailAsync(invitation.Email, acceptUrl, invitation.RoleName ?? "Member", invitation.ExpiresAt);

        return SwapResponse()
            .WithSuccessToast("Invitation resent!")
            .Build();
    }

    /// <summary>
    /// Accept invitation — public endpoint (no auth required). Creates user and signs in.
    /// </summary>
    [HttpGet("accept")]
    [AllowAnonymous]
    public async Task<IActionResult> Accept([FromRoute] string slug, [FromQuery] string token)
    {
        var invitation = await _db.Set<TeamInvitation>()
            .FirstOrDefaultAsync(i => i.Token == token && i.Status == InvitationStatus.Pending);

        if (invitation is null || invitation.ExpiresAt < DateTime.UtcNow)
        {
            ViewData["Error"] = "This invitation is invalid or has expired.";
            return SwapView(SwapViews.Invitation.AcceptInvitation, (TeamInvitation?)null);
        }

        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(invitation.Email);
        if (existingUser is not null)
        {
            // Mark as accepted since user exists
            invitation.Status = InvitationStatus.Accepted;
            invitation.AcceptedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Redirect($"/{slug}/login");
        }

        // Create user
        var user = new AppUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true,
            IsActive = true,
            IsEmailVerified = true,
            EmailVerifiedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            ViewData["Error"] = "Failed to create account. Please contact your administrator.";
            return SwapView(SwapViews.Invitation.AcceptInvitation, (TeamInvitation?)null);
        }

        // Assign role if specified
        if (!string.IsNullOrEmpty(invitation.RoleName))
        {
            await _userManager.AddToRoleAsync(user, invitation.RoleName);
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Notify the person who sent the invitation
        if (!string.IsNullOrEmpty(invitation.InvitedByUserId))
        {
            try
            {
                await _notifications.SendAsync(invitation.InvitedByUserId,
                    "Invitation accepted",
                    $"{invitation.Email} accepted your team invitation",
                    $"/{slug}/admin/invitation",
                    Notifications.Entities.NotificationType.Success);
            }
            catch { /* Don't block accept if notification fails */ }
        }

        // Redirect to login
        return Redirect($"/{slug}/login");
    }

    private async Task SendInvitationEmailAsync(string email, string acceptUrl, string roleName, DateTime expiresAt)
    {
        var htmlBody = _templateService.Render("TeamInvitation", new Dictionary<string, string>
        {
            ["InviterEmail"] = _currentUser.Email ?? "A team member",
            ["TenantName"] = _tenantContext.Slug ?? "unknown",
            ["RoleName"] = roleName,
            ["AcceptUrl"] = acceptUrl,
            ["ExpiresAt"] = expiresAt.ToString("MMMM d, yyyy")
        });

        await _emailService.SendAsync(new EmailMessage(
            email,
            $"You've been invited to join {_tenantContext.Slug}",
            htmlBody));
    }
}
