using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Tasks.DTOs;
using saas.Modules.Tasks.Entities;

namespace saas.Modules.Tasks.Services;

public interface ITaskService
{
    Task<List<TaskListItemDto>> GetListAsync(AgentTaskStatus? status = null, string? assigneeUserId = null, TaskPriority? priority = null, string? linkedEntityType = null);
    Task<AgentTask?> GetByIdAsync(Guid id);
    Task<AgentTask> CreateAsync(CreateTaskDto dto, string createdByUserId);
    Task UpdateAsync(EditTaskDto dto);
    Task CompleteAsync(Guid id, string completedByUserId);
    Task DeleteAsync(Guid id);
    Task<TaskWidgetDto> GetWidgetDataAsync();
    Task<List<TaskListItemDto>> GetLinkedTasksAsync(string entityType, Guid entityId);
}

public class TaskService : ITaskService
{
    private readonly TenantDbContext _db;

    public TaskService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<TaskListItemDto>> GetListAsync(
        AgentTaskStatus? status = null,
        string? assigneeUserId = null,
        TaskPriority? priority = null,
        string? linkedEntityType = null)
    {
        var query = _db.AgentTasks.AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrEmpty(assigneeUserId))
            query = query.Where(t => t.AssigneeUserId == assigneeUserId);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        if (!string.IsNullOrEmpty(linkedEntityType))
            query = query.Where(t => t.LinkedEntityType == linkedEntityType);

        var tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        var dtos = tasks.Select(MapToDto).ToList();
        await ResolveAssigneeNamesAsync(dtos);
        return dtos;
    }

    public async Task<AgentTask?> GetByIdAsync(Guid id)
    {
        return await _db.AgentTasks.FindAsync(id);
    }

    public async Task<AgentTask> CreateAsync(CreateTaskDto dto, string createdByUserId)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            Priority = dto.Priority,
            Status = AgentTaskStatus.Open,
            AssigneeUserId = dto.AssigneeUserId,
            LinkedEntityType = dto.LinkedEntityType,
            LinkedEntityId = dto.LinkedEntityId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdByUserId
        };

        _db.AgentTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task UpdateAsync(EditTaskDto dto)
    {
        var task = await _db.AgentTasks.FindAsync(dto.Id);
        if (task == null) return;

        task.Title = dto.Title;
        task.Description = dto.Description;
        task.DueDate = dto.DueDate;
        task.Priority = dto.Priority;
        task.Status = dto.Status;
        task.AssigneeUserId = dto.AssigneeUserId;
        task.LinkedEntityType = dto.LinkedEntityType;
        task.LinkedEntityId = dto.LinkedEntityId;

        await _db.SaveChangesAsync();
    }

    public async Task CompleteAsync(Guid id, string completedByUserId)
    {
        var task = await _db.AgentTasks.FindAsync(id);
        if (task == null) return;

        task.Status = AgentTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedByUserId = completedByUserId;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var task = await _db.AgentTasks.FindAsync(id);
        if (task == null) return;

        _db.AgentTasks.Remove(task);
        await _db.SaveChangesAsync();
    }

    public async Task<TaskWidgetDto> GetWidgetDataAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdueCount = await _db.AgentTasks
            .Where(t => t.DueDate != null
                && t.DueDate < today
                && (t.Status == AgentTaskStatus.Open || t.Status == AgentTaskStatus.InProgress))
            .CountAsync();

        var upcoming = await _db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Open || t.Status == AgentTaskStatus.InProgress)
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Take(5)
            .ToListAsync();

        var upcomingDtos = upcoming.Select(MapToDto).ToList();
        await ResolveAssigneeNamesAsync(upcomingDtos);
        return new TaskWidgetDto
        {
            OverdueCount = overdueCount,
            UpcomingTasks = upcomingDtos
        };
    }

    public async Task<List<TaskListItemDto>> GetLinkedTasksAsync(string entityType, Guid entityId)
    {
        var tasks = await _db.AgentTasks
            .Where(t => t.LinkedEntityType == entityType && t.LinkedEntityId == entityId)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ToListAsync();

        var dtos = tasks.Select(MapToDto).ToList();
        await ResolveAssigneeNamesAsync(dtos);
        return dtos;
    }

    private async Task ResolveAssigneeNamesAsync(List<TaskListItemDto> dtos)
    {
        var userIds = dtos.Where(d => d.AssigneeUserId != null).Select(d => d.AssigneeUserId!).Distinct().ToList();
        if (userIds.Count == 0) return;

        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync();

        var lookup = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Email ?? u.Id);
        foreach (var dto in dtos)
        {
            if (dto.AssigneeUserId != null && lookup.TryGetValue(dto.AssigneeUserId, out var name))
                dto.AssigneeName = name;
        }
    }

    private static TaskListItemDto MapToDto(AgentTask t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        DueDate = t.DueDate,
        Priority = t.Priority,
        Status = t.Status,
        AssigneeUserId = t.AssigneeUserId,
        LinkedEntityType = t.LinkedEntityType,
        LinkedEntityId = t.LinkedEntityId,
        CreatedAt = t.CreatedAt
    };
}
