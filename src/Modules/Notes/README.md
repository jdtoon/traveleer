# Notes Module

Example tenant module demonstrating the full vertical-slice pattern with CRUD operations.

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
