using saas.Modules.Tasks.Entities;

namespace saas.Modules.Tasks.DTOs;

public class TaskListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
    public AgentTaskStatus Status { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public string? LinkedEntityType { get; set; }
    public Guid? LinkedEntityId { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsOverdue => DueDate.HasValue
        && Status is AgentTaskStatus.Open or AgentTaskStatus.InProgress
        && DueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow);

    public bool IsDueToday => DueDate.HasValue
        && DueDate.Value == DateOnly.FromDateTime(DateTime.UtcNow);
}

public class CreateTaskDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public string? AssigneeUserId { get; set; }
    public string? LinkedEntityType { get; set; }
    public Guid? LinkedEntityId { get; set; }
}

public class EditTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
    public AgentTaskStatus Status { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? LinkedEntityType { get; set; }
    public Guid? LinkedEntityId { get; set; }
}

public class TaskWidgetDto
{
    public int OverdueCount { get; set; }
    public List<TaskListItemDto> UpcomingTasks { get; set; } = [];
}
