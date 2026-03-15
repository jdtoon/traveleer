using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Tasks.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Tasks;

public class TaskIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public TaskIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page ──

    [Fact]
    public async Task TasksPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/tasks");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Tasks");
        await response.AssertContainsAsync("+ New Task");
    }

    [Fact]
    public async Task TasksPage_ContainsTaskListTarget()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/tasks");

        response.AssertSuccess();
        await response.AssertContainsAsync("id=\"task-list\"");
    }

    [Fact]
    public async Task TasksPage_ContainsFilterControls()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/tasks");

        response.AssertSuccess();
        await response.AssertContainsAsync("All Statuses");
        await response.AssertContainsAsync("All Priorities");
        await response.AssertContainsAsync("All Types");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task NewTaskForm_RendersModalContent()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/new");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("New Task");
        await response.AssertContainsAsync("Title");
        await response.AssertContainsAsync("Priority");
        await response.AssertContainsAsync("Assignee");
        await response.AssertContainsAsync("Create Task");
    }

    [Fact]
    public async Task NewTaskForm_WithLinkedEntity_ShowsBadge()
    {
        var entityId = Guid.NewGuid();
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/new?linkedEntityType=Booking&linkedEntityId={entityId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Booking");
        await response.AssertContainsAsync(entityId.ToString());
    }

    // ── Layer 3: Data Flow ──

    [Fact]
    public async Task CreateTask_PersistsToDatabase()
    {
        var uniqueTitle = $"IntTest-{Guid.NewGuid():N}"[..25];

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = uniqueTitle,
            ["Description"] = "Integration test task",
            ["Priority"] = "2"
        });

        response.AssertSuccess();

        await using var db = OpenTenantDb();
        var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.Title == uniqueTitle);
        Assert.NotNull(task);
        Assert.Equal("Integration test task", task!.Description);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(AgentTaskStatus.Open, task.Status);
    }

    [Fact]
    public async Task TaskList_ShowsCreatedTask()
    {
        var entityType = $"Entity-{Guid.NewGuid():N}"[..16];

        // Seed a task directly
        await using (var db = OpenTenantDb())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "Visible-Task-List-Test",
                Priority = TaskPriority.Normal,
                Status = AgentTaskStatus.Open,
                LinkedEntityType = entityType,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/list?entityType={entityType}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Visible-Task-List-Test");
    }

    [Fact]
    public async Task TaskList_FilterByStatus_ReturnsFiltered()
    {
        var entityType = $"Entity-{Guid.NewGuid():N}"[..16];

        // Seed tasks
        await using (var db = OpenTenantDb())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "OpenFilterTest",
                Status = AgentTaskStatus.Open,
                LinkedEntityType = entityType,
                CreatedAt = DateTime.UtcNow
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "CompletedFilterTest",
                Status = AgentTaskStatus.Completed,
                LinkedEntityType = entityType,
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/list?status=0&entityType={entityType}");

        response.AssertSuccess();
        await response.AssertContainsAsync("OpenFilterTest");
        await response.AssertDoesNotContainAsync("CompletedFilterTest");
    }

    [Fact]
    public async Task TaskList_WhenMoreThanOnePage_PaginatesResults()
    {
        var prefix = $"PagedTask-{Guid.NewGuid():N}"[..12];
        var entityType = $"Entity-{Guid.NewGuid():N}"[..16];

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = Guid.NewGuid(),
                    Title = $"{prefix}-{index:D2}",
                    Priority = TaskPriority.Normal,
                    Status = AgentTaskStatus.Open,
                    LinkedEntityType = entityType,
                    CreatedAt = DateTime.UtcNow.AddMinutes(index)
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/list?entityType={entityType}");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync($"{prefix}-13");
        await firstPage.AssertContainsAsync("Next");

        var secondPage = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/list?entityType={entityType}&page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync($"{prefix}-01");
        await secondPage.AssertDoesNotContainAsync($"{prefix}-13");
    }

    [Fact]
    public async Task EditTask_ReturnsFormWithValues()
    {
        Guid taskId;
        await using (var db = OpenTenantDb())
        {
            var t = new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "EditableTask",
                Description = "Edit me",
                Priority = TaskPriority.High,
                Status = AgentTaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            };
            db.AgentTasks.Add(t);
            await db.SaveChangesAsync();
            taskId = t.Id;
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/edit/{taskId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Edit Task");
        await response.AssertContainsAsync("EditableTask");
        await response.AssertContainsAsync("Edit me");
    }

    [Fact]
    public async Task Widget_RendersTaskSummary()
    {
        // Seed overdue + upcoming
        await using (var db = OpenTenantDb())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "WidgetOverdue",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                Status = AgentTaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "WidgetUpcoming",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
                Status = AgentTaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/widget");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Tasks");
        await response.AssertContainsAsync("overdue");
    }

    [Fact]
    public async Task LinkedTasks_ReturnsMatchingTasks()
    {
        var entityId = Guid.NewGuid();
        await using (var db = OpenTenantDb())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "LinkedBookingTask",
                LinkedEntityType = "Booking",
                LinkedEntityId = entityId,
                Status = AgentTaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/tasks/linked?entityType=Booking&entityId={entityId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("LinkedBookingTask");
    }

    [Fact]
    public async Task TasksPage_WhenUnauthenticated_Redirects()
    {
        var unauthClient = _fixture.Factory.CreateDefaultClient();
        var response = await unauthClient.GetAsync($"/{TenantSlug}/tasks");

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found,
            $"Expected redirect but got {response.StatusCode}");
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
