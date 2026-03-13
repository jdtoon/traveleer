# Business Growth — Feature Specifications

Four features that help travel agencies understand performance and work as a team.

---

## Reporting & Analytics Module

### Overview

A dedicated Reports module that surfaces key business metrics through configurable dashboard widgets. Agents and managers see revenue trends, quote conversion rates, top clients and suppliers, and profitability analysis — all derived from existing Bookings, Quotes, and Clients data.

### Approach

No heavy data warehouse. Reports query existing `TenantDbContext` entities directly using read-only projections. Widgets are pre-defined (not user-configurable in v1) but can be toggled on/off per user preference.

### Entities

```
TenantDbContext:
  UserReportPreference  → which widgets a user has enabled/hidden
```

**UserReportPreference**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| UserId | string(450) | FK → AppUser.Id |
| WidgetKey | string(100) | e.g. "revenue.monthly", "quotes.conversion" |
| IsVisible | bool | User toggle |
| SortOrder | int | Dashboard layout order |

### Pre-defined Widgets (v1)

| Widget Key | Title | Data Source | Visualization |
|------------|-------|-------------|---------------|
| revenue.monthly | Monthly Revenue | Bookings.TotalSelling by month | Bar chart |
| revenue.ytd | Year-to-Date Revenue | Bookings.TotalSelling YTD vs prior year | Stat cards |
| bookings.status | Bookings by Status | Bookings grouped by Status | Donut chart |
| bookings.recent | Recent Bookings | Last 10 bookings | Table |
| quotes.conversion | Quote Conversion Rate | Quotes Accepted / Quotes Sent | Percentage + trend |
| quotes.pipeline | Quote Pipeline | Quotes by Status | Horizontal bar |
| clients.top | Top Clients | Clients by total booking value | Ranked table |
| suppliers.top | Top Suppliers | Suppliers by total booking item cost | Ranked table |
| profitability.summary | Profitability Summary | TotalSelling - TotalCost across bookings | Stat cards |
| profitability.by_booking | Profit by Booking | Per-booking margin | Sorted table |

### Module Registration

```csharp
public class ReportsModule : IModule
{
    public string Name => "Reports";
    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("reports", "Reports & Analytics", MinPlanSlug: "starter")
    ];
    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("reports.read", "View Reports", "Reports", 0),
    ];
}
```

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/reports` | Dashboard page with widgets |
| GET | `/{slug}/reports/widget/{key}` | Individual widget partial (for lazy load) |
| GET | `/{slug}/reports/revenue` | Revenue detail page |
| GET | `/{slug}/reports/bookings` | Bookings analysis page |
| GET | `/{slug}/reports/quotes` | Quote pipeline analysis |
| GET | `/{slug}/reports/clients` | Client analysis page |
| GET | `/{slug}/reports/profitability` | Profitability analysis page |
| POST | `/{slug}/reports/preferences` | Save widget visibility/order |

### UI/UX Design

**Dashboard Page:**
- Grid of widget cards, each self-loading via `hx-get` on `load`
- Each widget has a loading spinner that resolves to its chart/table
- Date range picker (This Month / This Quarter / This Year / Custom) applies to all widgets
- Widgets are pure server-rendered — charts via DaisyUI stat components and HTML tables, not JavaScript chart libraries (keep it simple and consistent)

**Detail Pages:**
- Each analysis area (revenue, bookings, quotes, clients, profitability) is a full page with filters, sortable tables, and summary stats
- Export to CSV button for tabular data

### HTMX Interactions

- Dashboard widgets load independently via `hx-trigger="load"`
- Date range change triggers `reports.refresh` which refreshes all visible widgets
- Widget preferences save inline via `hx-post`

### Dependencies

- **Bookings** — revenue, profitability, status data
- **Quotes** — pipeline, conversion metrics
- **Clients** — top client analysis
- **Settings** — currency for formatting

---

## Task Management

### Overview

A lightweight task system for tracking follow-ups across bookings, quotes, and clients. Tasks have due dates, assignees, and priorities. A dashboard widget shows overdue and upcoming tasks.

### Entities

```
TenantDbContext:
  AgentTask             → a follow-up or to-do item linked to a booking, quote, or client
```

**AgentTask** (named `AgentTask` to avoid collision with `System.Threading.Tasks.Task`)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| Title | string(200) | Required |
| Description | string(2000)? | Optional details |
| DueDate | DateOnly? | When this task is due |
| Priority | TaskPriority | Low, Normal, High, Urgent |
| Status | AgentTaskStatus | Open, InProgress, Completed, Cancelled |
| AssigneeUserId | string(450)? | FK → AppUser.Id (nullable = unassigned) |
| LinkedEntityType | string(50)? | "Booking", "Quote", "Client" |
| LinkedEntityId | Guid? | FK to the linked entity |
| CompletedAt | DateTime? | When marked complete |
| CompletedByUserId | string(450)? | Who completed it |

**Enums:**
- `TaskPriority`: Low = 0, Normal = 1, High = 2, Urgent = 3
- `AgentTaskStatus`: Open = 0, InProgress = 1, Completed = 2, Cancelled = 3

### Module Registration

```csharp
public class TasksModule : IModule
{
    public string Name => "Tasks";
    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("tasks", "Task Management", MinPlanSlug: "starter")
    ];
    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("tasks.read", "View Tasks", "Tasks", 0),
        new("tasks.create", "Create Tasks", "Tasks", 1),
        new("tasks.edit", "Edit Tasks", "Tasks", 2),
        new("tasks.delete", "Delete Tasks", "Tasks", 3),
    ];
}
```

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/tasks` | Task list page |
| GET | `/{slug}/tasks/list` | Task list partial (filtered) |
| GET | `/{slug}/tasks/new` | New task form |
| POST | `/{slug}/tasks/create` | Create task |
| GET | `/{slug}/tasks/edit/{id}` | Edit task form |
| POST | `/{slug}/tasks/update/{id}` | Update task |
| POST | `/{slug}/tasks/complete/{id}` | Mark complete |
| POST | `/{slug}/tasks/delete/{id}` | Delete task |
| GET | `/{slug}/tasks/widget` | Dashboard widget partial |

### UI/UX Design

**Task List Page:**
- Filterable by status (Open/Completed), assignee, priority, linked entity type
- Sorted by due date (overdue first, then upcoming)
- Each row: title, due date (red if overdue, yellow if today), priority badge, assignee avatar, linked entity link
- Quick-complete button per row (checkmark icon)

**Dashboard Widget:**
- Shows count of overdue tasks and next 5 upcoming tasks
- Click through to full task list

**Task Form Modal:**
- Title, description, due date, priority dropdown, assignee dropdown (team members), link to entity (optional picker)

**Context Integration:**
- Booking detail page: "Tasks" tab showing tasks linked to that booking, with "Add Task" button
- Quote detail page: same pattern
- Client detail page: same pattern

### HTMX Interactions

- Task list refreshes via `tasks.refresh` trigger
- Complete button sends `hx-post` and triggers `tasks.refresh`
- Dashboard widget loads via `hx-trigger="load, tasks.refresh from:body"`

### Dependencies

- **Bookings** — linked entity
- **Quotes** — linked entity
- **Clients** — linked entity
- **Dashboard** — widget integration
- **Auth** — assignee user list

---

## Team Collaboration

### Overview

Activity tracking and booking ownership for team coordination. Every significant action on a booking is logged as an activity entry. Bookings can be assigned to team members. Internal notes/comments form a conversation thread per booking.

### Entities

```
TenantDbContext:
  ActivityEntry         → audit-like log entry visible to team members
  BookingAssignment     → which user(s) own a booking
  BookingComment        → internal conversation thread per booking
```

**ActivityEntry**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid | FK → Booking |
| UserId | string(450) | Who performed the action |
| UserName | string(200) | Display name (denormalized) |
| ActivityType | ActivityType | StatusChange, ItemAdded, ItemUpdated, SupplierRequested, VoucherGenerated, PaymentRecorded, CommentAdded, Assigned |
| Summary | string(500) | Human-readable description |
| CreatedAt | DateTime | |

**BookingAssignment**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid | FK → Booking |
| UserId | string(450) | FK → AppUser.Id |
| AssignedAt | DateTime | |
| AssignedByUserId | string(450)? | Who made the assignment |

**BookingComment**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid | FK → Booking |
| UserId | string(450) | Author |
| UserName | string(200) | Denormalized display name |
| Content | string(2000) | Comment text |
| CreatedAt | DateTime | |

### Module Integration

Part of the Bookings module — not a standalone module. These entities support the booking workflow.

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/bookings/activity/{bookingId}` | Activity feed partial |
| GET | `/{slug}/bookings/comments/{bookingId}` | Comments thread partial |
| POST | `/{slug}/bookings/comments/create/{bookingId}` | Add comment |
| POST | `/{slug}/bookings/assign/{bookingId}` | Assign user to booking |
| POST | `/{slug}/bookings/unassign/{bookingId}/{userId}` | Remove assignment |

### UI/UX Design

**Booking Detail — Activity Tab:**
- Reverse-chronological timeline of all activity
- Each entry: icon (by type), user name, summary, timestamp
- Auto-generated from booking state changes (not manually entered)

**Booking Detail — Comments Tab:**
- Thread of internal comments
- Text input at the bottom with "Post" button
- Each comment: user avatar, name, content, timestamp

**Booking Detail — Assignment:**
- Header shows assigned user(s) with avatar badges
- "Assign" button opens a dropdown of team members
- Multiple assignees supported

**Booking List Enhancement:**
- Show assigned user avatar(s) on each booking row
- Filter bookings by "Assigned to me"

### HTMX Interactions

- Activity feed loads via `hx-trigger="load, bookings.activity.refresh from:body"`
- Comments thread refreshes via `bookings.comments.refresh`
- Assignment changes trigger both `bookings.activity.refresh` and header update

### Dependencies

- **Bookings** — parent entity
- **Auth** — team member list for assignment
- **Notifications** — optional: notify assigned users of activity

---

## Audit Dashboard

### Overview

Surface the existing `AuditDbContext` data in a user-friendly, tenant-facing timeline. Currently, audit entries are written by infrastructure but not exposed in the tenant UI. This feature adds a read-only view that lets admins see who changed what and when.

### Approach

No new entities. The existing `AuditEntry` table in `AuditDbContext` already captures entity changes with before/after values. This feature adds views to query and display that data, filtered by tenant.

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/audit` | Audit log page |
| GET | `/{slug}/audit/list` | Audit list partial (filtered, paginated) |
| GET | `/{slug}/audit/details/{id}` | Detail modal showing before/after diff |

### UI/UX Design

**Audit Log Page:**
- Filterable by: entity type, user, date range, action (Created/Updated/Deleted)
- Paginated table: timestamp, user, entity type, entity name/ref, action
- Click row to open detail modal

**Detail Modal:**
- Shows before/after values for each changed field
- Changed fields highlighted
- Unchanged fields shown in muted text

**Access Control:**
- Only users with `audit.read` permission (typically Admins only)
- This module adds a `audit.read` permission via the existing Audit module

### Dependencies

- **Audit** (framework module) — data source
- **Auth** — permission gating
