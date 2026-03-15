using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Tasks.DTOs;
using saas.Modules.Tasks.Entities;
using saas.Modules.Tasks.Services;
using Xunit;

namespace saas.Tests.Modules.Tasks;

public class TaskServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private TaskService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _service = new TaskService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── CreateAsync ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsTask()
    {
        var dto = new CreateTaskDto
        {
            Title = "Follow up with client",
            Description = "Confirm booking details",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            Priority = TaskPriority.High
        };

        var result = await _service.CreateAsync(dto, "user-1");

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Follow up with client", result.Title);
        Assert.Equal("Confirm booking details", result.Description);
        Assert.Equal(TaskPriority.High, result.Priority);
        Assert.Equal(AgentTaskStatus.Open, result.Status);
        Assert.Equal("user-1", result.CreatedBy);
    }

    [Fact]
    public async Task CreateAsync_SetsLinkedEntity()
    {
        var entityId = Guid.NewGuid();
        var dto = new CreateTaskDto
        {
            Title = "Send invoice",
            LinkedEntityType = "Booking",
            LinkedEntityId = entityId
        };

        var result = await _service.CreateAsync(dto, "user-1");

        Assert.Equal("Booking", result.LinkedEntityType);
        Assert.Equal(entityId, result.LinkedEntityId);
    }

    [Fact]
    public async Task CreateAsync_SetsAssignee()
    {
        var dto = new CreateTaskDto
        {
            Title = "Assigned task",
            AssigneeUserId = "user-2"
        };

        var result = await _service.CreateAsync(dto, "user-1");

        Assert.Equal("user-2", result.AssigneeUserId);
    }

    // ── GetByIdAsync ────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsTask()
    {
        var task = await _service.CreateAsync(new CreateTaskDto { Title = "Test" }, "u1");

        var found = await _service.GetByIdAsync(task.Id);

        Assert.NotNull(found);
        Assert.Equal("Test", found!.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var found = await _service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    // ── GetListAsync ────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_ReturnsAllTasks()
    {
        await _service.CreateAsync(new CreateTaskDto { Title = "Task 1" }, "u1");
        await _service.CreateAsync(new CreateTaskDto { Title = "Task 2" }, "u1");
        await _service.CreateAsync(new CreateTaskDto { Title = "Task 3" }, "u1");

        var list = await _service.GetListAsync();

        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_FiltersBy_Status()
    {
        await _service.CreateAsync(new CreateTaskDto { Title = "Open" }, "u1");
        var completed = await _service.CreateAsync(new CreateTaskDto { Title = "Done" }, "u1");
        await _service.CompleteAsync(completed.Id, "u1");

        var openTasks = await _service.GetListAsync(status: AgentTaskStatus.Open);

        Assert.Single(openTasks.Items);
        Assert.Equal("Open", openTasks.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_FiltersBy_Assignee()
    {
        await _service.CreateAsync(new CreateTaskDto { Title = "For A", AssigneeUserId = "user-a" }, "u1");
        await _service.CreateAsync(new CreateTaskDto { Title = "For B", AssigneeUserId = "user-b" }, "u1");

        var result = await _service.GetListAsync(assigneeUserId: "user-a");

        Assert.Single(result.Items);
        Assert.Equal("For A", result.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_FiltersBy_Priority()
    {
        await _service.CreateAsync(new CreateTaskDto { Title = "Low", Priority = TaskPriority.Low }, "u1");
        await _service.CreateAsync(new CreateTaskDto { Title = "Urgent", Priority = TaskPriority.Urgent }, "u1");

        var result = await _service.GetListAsync(priority: TaskPriority.Urgent);

        Assert.Single(result.Items);
        Assert.Equal("Urgent", result.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_FiltersBy_LinkedEntityType()
    {
        await _service.CreateAsync(new CreateTaskDto { Title = "Booking task", LinkedEntityType = "Booking" }, "u1");
        await _service.CreateAsync(new CreateTaskDto { Title = "Quote task", LinkedEntityType = "Quote" }, "u1");

        var result = await _service.GetListAsync(linkedEntityType: "Booking");

        Assert.Single(result.Items);
        Assert.Equal("Booking task", result.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_OrdersByPriorityDesc_ThenDueDate()
    {
        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Low soon",
            Priority = TaskPriority.Low,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        }, "u1");
        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Urgent later",
            Priority = TaskPriority.Urgent,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        }, "u1");

        var result = await _service.GetListAsync();

        Assert.Equal("Urgent later", result.Items[0].Title);
        Assert.Equal("Low soon", result.Items[1].Title);
    }

    // ── UpdateAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var task = await _service.CreateAsync(new CreateTaskDto { Title = "Original" }, "u1");

        await _service.UpdateAsync(new EditTaskDto
        {
            Id = task.Id,
            Title = "Updated Title",
            Description = "New description",
            Priority = TaskPriority.Urgent,
            Status = AgentTaskStatus.InProgress,
            DueDate = new DateOnly(2026, 6, 15)
        });

        var updated = await _service.GetByIdAsync(task.Id);
        Assert.Equal("Updated Title", updated!.Title);
        Assert.Equal("New description", updated.Description);
        Assert.Equal(TaskPriority.Urgent, updated.Priority);
        Assert.Equal(AgentTaskStatus.InProgress, updated.Status);
        Assert.Equal(new DateOnly(2026, 6, 15), updated.DueDate);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_DoesNotThrow()
    {
        await _service.UpdateAsync(new EditTaskDto
        {
            Id = Guid.NewGuid(),
            Title = "Ghost"
        });
        // No exception = pass
    }

    // ── CompleteAsync ───────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SetsStatusAndCompletedFields()
    {
        var task = await _service.CreateAsync(new CreateTaskDto { Title = "To complete" }, "u1");

        await _service.CompleteAsync(task.Id, "completer-1");

        var completed = await _service.GetByIdAsync(task.Id);
        Assert.Equal(AgentTaskStatus.Completed, completed!.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal("completer-1", completed.CompletedByUserId);
    }

    [Fact]
    public async Task CompleteAsync_NonExistent_DoesNotThrow()
    {
        await _service.CompleteAsync(Guid.NewGuid(), "u1");
    }

    // ── DeleteAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesTask()
    {
        var task = await _service.CreateAsync(new CreateTaskDto { Title = "To delete" }, "u1");

        await _service.DeleteAsync(task.Id);

        var found = await _service.GetByIdAsync(task.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        await _service.DeleteAsync(Guid.NewGuid());
    }

    // ── GetWidgetDataAsync ──────────────────────────────────

    [Fact]
    public async Task GetWidgetDataAsync_CountsOverdue()
    {
        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Overdue",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5))
        }, "u1");
        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Future",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
        }, "u1");

        var widget = await _service.GetWidgetDataAsync();

        Assert.Equal(1, widget.OverdueCount);
    }

    [Fact]
    public async Task GetWidgetDataAsync_ReturnsUpTo5Tasks()
    {
        for (int i = 0; i < 7; i++)
        {
            await _service.CreateAsync(new CreateTaskDto
            {
                Title = $"Task {i}",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(i))
            }, "u1");
        }

        var widget = await _service.GetWidgetDataAsync();

        Assert.Equal(5, widget.UpcomingTasks.Count);
    }

    [Fact]
    public async Task GetWidgetDataAsync_ExcludesCompletedFromOverdue()
    {
        var task = await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Done overdue",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3))
        }, "u1");
        await _service.CompleteAsync(task.Id, "u1");

        var widget = await _service.GetWidgetDataAsync();

        Assert.Equal(0, widget.OverdueCount);
    }

    // ── GetLinkedTasksAsync ─────────────────────────────────

    [Fact]
    public async Task GetLinkedTasksAsync_ReturnsOnlyMatchingLinks()
    {
        var bookingId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();

        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Booking task",
            LinkedEntityType = "Booking",
            LinkedEntityId = bookingId
        }, "u1");
        await _service.CreateAsync(new CreateTaskDto
        {
            Title = "Quote task",
            LinkedEntityType = "Quote",
            LinkedEntityId = quoteId
        }, "u1");

        var result = await _service.GetLinkedTasksAsync("Booking", bookingId);

        Assert.Single(result);
        Assert.Equal("Booking task", result[0].Title);
    }

    [Fact]
    public async Task GetLinkedTasksAsync_ReturnsEmpty_WhenNoMatch()
    {
        var result = await _service.GetLinkedTasksAsync("Client", Guid.NewGuid());

        Assert.Empty(result);
    }

    // ── DTO computed properties ─────────────────────────────

    [Fact]
    public void TaskListItemDto_IsOverdue_WhenPastDueAndOpen()
    {
        var dto = new TaskListItemDto
        {
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = AgentTaskStatus.Open
        };

        Assert.True(dto.IsOverdue);
    }

    [Fact]
    public void TaskListItemDto_IsNotOverdue_WhenCompleted()
    {
        var dto = new TaskListItemDto
        {
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = AgentTaskStatus.Completed
        };

        Assert.False(dto.IsOverdue);
    }

    [Fact]
    public void TaskListItemDto_IsDueToday()
    {
        var dto = new TaskListItemDto
        {
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        Assert.True(dto.IsDueToday);
    }
}
