using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Tasks.DTOs;
using saas.Modules.Tasks.Entities;
using saas.Modules.Tasks.Events;
using saas.Modules.Tasks.Services;
using saas.Modules.TenantAdmin.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Tasks.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(TaskFeatures.Tasks)]
public class TaskController : SwapController
{
    private readonly ITaskService _taskService;
    private readonly ITenantAdminService _adminService;
    private readonly ICurrentUser _currentUser;

    public TaskController(ITaskService taskService, ITenantAdminService adminService, ICurrentUser currentUser)
    {
        _taskService = taskService;
        _adminService = adminService;
        _currentUser = currentUser;
    }

    [HttpGet("{slug}/tasks")]
    [HasPermission(TaskPermissions.TasksRead)]
    public async Task<IActionResult> Index(AgentTaskStatus? status, string? assignee, TaskPriority? priority, string? entityType)
    {
        var tasks = await _taskService.GetListAsync(status, assignee, priority, entityType);
        ViewBag.StatusFilter = status;
        ViewBag.AssigneeFilter = assignee;
        ViewBag.PriorityFilter = priority;
        ViewBag.EntityTypeFilter = entityType;
        return SwapView("Index", tasks);
    }

    [HttpGet("{slug}/tasks/list")]
    [HasPermission(TaskPermissions.TasksRead)]
    public async Task<IActionResult> List(AgentTaskStatus? status, string? assignee, TaskPriority? priority, string? entityType)
    {
        var tasks = await _taskService.GetListAsync(status, assignee, priority, entityType);
        return PartialView("_TaskList", tasks);
    }

    [HttpGet("{slug}/tasks/new")]
    [HasPermission(TaskPermissions.TasksCreate)]
    public async Task<IActionResult> New(string? linkedEntityType, Guid? linkedEntityId)
    {
        var users = await _adminService.GetUsersAsync(page: 1, pageSize: 100);
        ViewBag.Users = users.Items;
        var dto = new CreateTaskDto
        {
            LinkedEntityType = linkedEntityType,
            LinkedEntityId = linkedEntityId
        };
        return PartialView("_CreateForm", dto);
    }

    [HttpPost("{slug}/tasks/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(TaskPermissions.TasksCreate)]
    public async Task<IActionResult> Create([FromForm] CreateTaskDto dto)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        await _taskService.CreateAsync(dto, userId);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Task created.")
            .WithTrigger(TaskEvents.Refresh)
            .Build();
    }

    [HttpGet("{slug}/tasks/edit/{id:guid}")]
    [HasPermission(TaskPermissions.TasksEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var task = await _taskService.GetByIdAsync(id);
        if (task == null) return NotFound();

        var users = await _adminService.GetUsersAsync(page: 1, pageSize: 100);
        ViewBag.Users = users.Items;

        var dto = new EditTaskDto
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = task.Priority,
            Status = task.Status,
            AssigneeUserId = task.AssigneeUserId,
            LinkedEntityType = task.LinkedEntityType,
            LinkedEntityId = task.LinkedEntityId
        };
        return PartialView("_EditForm", dto);
    }

    [HttpPost("{slug}/tasks/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(TaskPermissions.TasksEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] EditTaskDto dto)
    {
        dto.Id = id;
        await _taskService.UpdateAsync(dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Task updated.")
            .WithTrigger(TaskEvents.Refresh)
            .Build();
    }

    [HttpPost("{slug}/tasks/complete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(TaskPermissions.TasksEdit)]
    public async Task<IActionResult> Complete(Guid id)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        await _taskService.CompleteAsync(id, userId);
        var tasks = await _taskService.GetListAsync();
        return PartialView("_TaskList", tasks);
    }

    [HttpPost("{slug}/tasks/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(TaskPermissions.TasksDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _taskService.DeleteAsync(id);
        var tasks = await _taskService.GetListAsync();
        return PartialView("_TaskList", tasks);
    }

    [HttpGet("{slug}/tasks/widget")]
    [HasPermission(TaskPermissions.TasksRead)]
    public async Task<IActionResult> Widget()
    {
        var widget = await _taskService.GetWidgetDataAsync();
        return PartialView("_Widget", widget);
    }

    [HttpGet("{slug}/tasks/linked")]
    [HasPermission(TaskPermissions.TasksRead)]
    public async Task<IActionResult> LinkedTasks(string entityType, Guid entityId)
    {
        var tasks = await _taskService.GetLinkedTasksAsync(entityType, entityId);
        return PartialView("_TaskList", tasks);
    }
}
