using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Events;
using saas.Modules.Bookings.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BookingFeatures.Bookings)]
[Route("{slug}/bookings")]
public class CollaborationController : SwapController
{
    private readonly ICollaborationService _service;
    private readonly ICurrentUser _currentUser;

    public CollaborationController(ICollaborationService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("activity/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Activity(Guid bookingId)
    {
        var model = await _service.GetActivityFeedAsync(bookingId);
        return PartialView("_ActivityFeed", model);
    }

    [HttpGet("comments/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Comments(Guid bookingId)
    {
        var model = await _service.GetCommentsAsync(bookingId);
        return PartialView("_CommentThread", model);
    }

    [HttpPost("comments/create/{bookingId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> CreateComment(Guid bookingId, [FromForm] CreateCommentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return SwapResponse()
                .WithErrorToast("Comment cannot be empty.")
                .Build();
        }

        var userId = _currentUser.UserId ?? "unknown";
        var userName = _currentUser.DisplayName ?? _currentUser.Email ?? "Unknown";

        await _service.AddCommentAsync(bookingId, userId, userName, dto.Content.Trim());

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Comment added.")
            .WithTrigger(BookingEvents.CommentsRefresh)
            .WithTrigger(BookingEvents.ActivityRefresh)
            .Build();
    }

    [HttpGet("assignments/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Assignments(Guid bookingId)
    {
        var model = await _service.GetAssignmentsAsync(bookingId);
        return PartialView("_Assignments", model);
    }

    [HttpPost("assign/{bookingId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> Assign(Guid bookingId, [FromForm] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return SwapResponse()
                .WithErrorToast("Please select a team member.")
                .Build();
        }

        var assignedBy = _currentUser.UserId;
        await _service.AssignUserAsync(bookingId, userId, assignedBy);

        var model = await _service.GetAssignmentsAsync(bookingId);
        return SwapResponse()
            .WithView("_Assignments", model)
            .WithSuccessToast("Team member assigned.")
            .WithTrigger(BookingEvents.ActivityRefresh)
            .Build();
    }

    [HttpPost("unassign/{bookingId:guid}/{userId}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> Unassign(Guid bookingId, string userId)
    {
        await _service.UnassignUserAsync(bookingId, userId);

        var model = await _service.GetAssignmentsAsync(bookingId);
        return SwapResponse()
            .WithView("_Assignments", model)
            .WithSuccessToast("Team member unassigned.")
            .WithTrigger(BookingEvents.ActivityRefresh)
            .Build();
    }
}
