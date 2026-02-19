# saas

A modular monolith starter template powered by **Swap.Htmx** for building server-driven reactive web applications with **DaisyUI 5** and **Tailwind CSS v4**.

## Features

- 🎨 **DaisyUI 5** - Beautiful, responsive components out of the box
- 🌊 **Tailwind CSS v4** - Utility-first CSS via browser runtime
- ⚡ **HTMX** - Server-driven interactivity without complex JavaScript
- 🔧 **Source Generators** - Type-safe events and view constants
- 📦 **Modular Architecture** - Self-contained feature modules
- 🐳 **Docker Ready** - Production-ready containerization
- 📱 **Mobile Responsive** - DaisyUI drawer pattern for mobile navigation

## Quick Start

```bash
# Restore LibMan packages (HTMX, DaisyUI, Tailwind)
cd src
libman restore

# Create initial migrations (Core, Audit, Tenant)
dotnet ef migrations add InitialCore --context CoreDbContext --output-dir Data/Core/Migrations --project src/saas.csproj --startup-project src/saas.csproj
dotnet ef migrations add InitialAudit --context AuditDbContext --output-dir Data/Audit/Migrations --project src/saas.csproj --startup-project src/saas.csproj
dotnet ef migrations add InitialTenant --context TenantDbContext --output-dir Data/Tenant/Migrations --project src/saas.csproj --startup-project src/saas.csproj

# Run the application
dotnet run
```

## Project Structure

```
saas/
├── src/
│   ├── Controllers/           # Shared controllers
│   ├── Data/                  # Database context & configurations
│   ├── Infrastructure/        # Cross-cutting concerns
│   ├── Modules/               # Feature modules (self-contained)
│   │   └── Notes/             # Sample module
│   │       ├── Controllers/
│   │       ├── Entities/
│   │       ├── Events/
│   │       ├── Services/
│   │       └── Views/
│   ├── Views/                 # Shared views & layout
│   └── wwwroot/               # Static assets
│       └── lib/               # LibMan packages (DaisyUI, Tailwind, HTMX)
├── tests/                     # Integration tests
└── docker-compose.yml
```

## UI Framework

This template uses **DaisyUI 5** with **Tailwind CSS v4** for styling:

- **Theme**: `corporate` (professional light theme) - change via `data-theme` on `<body>`
- **Layout**: Responsive drawer pattern (sidebar on desktop, hamburger menu on mobile)
- **Components**: Cards, buttons, modals, forms, toasts - all from DaisyUI

### Available Themes

Change the theme by modifying `data-theme` in `_Layout.cshtml`:

```html
<body data-theme="corporate">  <!-- Light professional -->
<body data-theme="business">   <!-- Dark professional -->
<body data-theme="cupcake">    <!-- Light playful -->
<body data-theme="dracula">    <!-- Dark purple -->
```

See [DaisyUI Themes](https://daisyui.com/docs/themes/) for all options.

## Adding a New Module

1. Create module folder: `Modules/[ModuleName]/`
2. Add entity, service, controller, events, and views
3. Register in `Program.cs`: `builder.Services.Add[ModuleName]Module();`
4. Add event config in `Infrastructure/MvcExtensions.cs`
5. Add view mapping in `Infrastructure/ModuleViewLocationExpander.cs`
6. Register any module entity configuration in TenantDbContext (Data/Tenant/Configurations)
7. Add navigation in `_Layout.cshtml`

## Key Concepts

### Source Generators (Zero Magic Strings)

This template is pre-configured with source generators that create type-safe constants at compile time.

**Setup in `.csproj`:**
```xml
<ItemGroup>
  <AdditionalFiles Include="Views\**\*.cshtml" />
  <AdditionalFiles Include="Modules\**\Views\**\*.cshtml" />
</ItemGroup>
```

**Type-safe Events:**
```csharp
// Modules/Notes/Events/NotesEvents.cs
[SwapEventSource]
public static partial class NotesEvents
{
    public const string NotesListChanged = "notes.listChanged";
}
// Generated: NotesEvents.Notes.ListChanged → EventKey("notes.listChanged")

// Usage — type-safe, refactorable
return SwapEvent(NotesEvents.Notes.ListChanged, payload).Build();
```

**Auto-generated View & Element Constants:**
```csharp
// Auto-generated from your .cshtml files
builder.AlsoUpdate(SwapElements.NotesList, SwapViews.Notes.List, notes);
// Instead of magic strings: builder.AlsoUpdate("notes-list", "_List", notes);
```

Generated files are output to `obj/Generated/` — check there to see what's available.

### Server-Driven UI
All UI updates flow through server-rendered HTML partials. Use HTMX attributes for interactivity:
- `hx-get`/`hx-post` for requests
- `hx-target` for response destination
- `hx-trigger` for event-based updates

### Event System
Events trigger reactive updates across the UI:
```csharp
// Define events
[SwapEventSource]
public static partial class NotesEvents
{
    public const string NoteCreated = "note.created";
}

// Trigger from controller
return SwapResponse()
    .WithTrigger(NotesEvents.Note.Created)
    .Build();

// React in views
<div hx-get="/Notes/List" 
     hx-trigger="@NotesEvents.List.Changed from:body">
```

## Commands

```bash
# Development
dotnet run

# Add migration (Core)
dotnet ef migrations add [MigrationName] --context CoreDbContext --output-dir Data/Core/Migrations --project src/saas.csproj --startup-project src/saas.csproj

# Add migration (Audit)
dotnet ef migrations add [MigrationName] --context AuditDbContext --output-dir Data/Audit/Migrations --project src/saas.csproj --startup-project src/saas.csproj

# Add migration (Tenant)
dotnet ef migrations add [MigrationName] --context TenantDbContext --output-dir Data/Tenant/Migrations --project src/saas.csproj --startup-project src/saas.csproj

# Apply migrations (Core)
dotnet ef database update --context CoreDbContext --project src/saas.csproj --startup-project src/saas.csproj

# Apply migrations (Audit)
dotnet ef database update --context AuditDbContext --project src/saas.csproj --startup-project src/saas.csproj

# Apply migrations (Tenant)
dotnet ef database update --context TenantDbContext --project src/saas.csproj --startup-project src/saas.csproj

# Docker
docker-compose up -d
```

## Learn More

- [Swap.Htmx Documentation](https://github.com/jdtoon/swap)
- [HTMX Documentation](https://htmx.org)
