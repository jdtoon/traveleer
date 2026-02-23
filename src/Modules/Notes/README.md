# Notes Module (Example — Replace With Your Own)

> **This is a sample module** included to demonstrate the full vertical-slice pattern. When starting a new project, delete this module and create your own domain modules using the same structure.

## What This Demonstrates

- **IModule implementation** — features, permissions, default role mappings, view paths
- **Tenant-scoped entities** — `Note` entity stored in per-tenant SQLite DB
- **EF configuration** — `ITenantEntityConfiguration` marker for auto-discovery
- **Plan-gated feature** — `notes` feature requires the `starter` plan
- **Granular permissions** — 4 CRUD permissions with role-based defaults
- **Domain events** — MassTransit events for note creation
- **Swap.Htmx views** — Full CRUD with HTMX partial swaps and modals

## Structure

```
Notes/
├── NotesModule.cs          — IModule implementation (features, permissions, role mappings)
├── Entities/
│   └── Note.cs             — Note entity (tenant DB)
├── Data/
│   └── NoteConfiguration.cs — EF config (ITenantEntityConfiguration)
├── Services/
│   └── NotesService.cs     — Business logic
├── Views/
│   └── Notes/              — Razor views and partials
└── Controllers/ (uses shared controller discovery)
```

## Feature: `notes` (MinPlanSlug: `starter`)

Available from the Starter plan and above.

## Permissions

| Key | Name | Default Role |
|-----|------|-------------|
| `notes.read` | View Notes | Member |
| `notes.create` | Create Notes | Member |
| `notes.edit` | Edit Notes | Member |
| `notes.delete` | Delete Notes | Admin only |
