# Phase 2 - Clients Module Spec

## Goal

Port AgencyCardPro client management into Traveleer as a clean tenant-scoped module and use it as one of the first practical migration waves.

## Source Reference

AgencyCardPro client module lives in:

- `c:/dev/agencycardpro/src/Modules/Clients/Controllers/ClientController.cs`
- `c:/dev/agencycardpro/src/Modules/Clients/Services/ClientService.cs`
- `c:/dev/agencycardpro/src/Modules/Clients/Entities/Client.cs`
- `c:/dev/agencycardpro/src/Modules/Clients/Views/*`

## Why It Is A Good First Module

- core CRUD behavior is well-defined
- lower complexity than quotes or bookings
- useful dependency for later modules
- exposes the Traveleer module pattern clearly

## Target Module Location

- `src/Modules/Clients/`

Recommended structure:

- `ClientsModule.cs`
- `Controllers/ClientController.cs`
- `Data/ClientConfiguration.cs`
- `DTOs/ClientDto.cs`
- `Entities/Client.cs`
- `Events/ClientEvents.cs`
- `Services/ClientService.cs`
- `Views/Client/Index.cshtml`
- `Views/Client/_List.cshtml`
- `Views/Client/_Form.cshtml`
- `Views/Client/_DeleteConfirm.cshtml`
- optional `Views/Client/_Details.cshtml`

## Source Behavior Worth Preserving

From the AgencyCardPro module:

- paginated list and search
- duplicate email checks
- details view with related history
- quick selection use case for downstream quote workflows

## Target Behavior In Traveleer

### Core CRUD

- list clients for the current tenant
- search/filter by name, company, email, phone, country if retained
- create/edit/delete through HTMX modal flows
- details panel or modal for client history

### Downstream Integration

Later modules should be able to use clients in:

- quote builders
- booking flows
- document output context

### Multi-Tenant Rule

All clients are tenant-owned. No client record can leak across tenant boundaries.

## Data Notes

AgencyCardPro uses a single app database and integer IDs. Traveleer should use tenant-owned entities in `TenantDbContext`, preferably with GUID IDs unless a later ADR says otherwise.

Suggested fields:

- `Id`
- `Name`
- `Company`
- `Email`
- `Phone`
- `Address`
- `Country`
- `Notes`
- audit fields

## Controller Surface

Suggested routes:

- `GET /{slug}/clients`
- `GET /{slug}/clients/list`
- `GET /{slug}/clients/new`
- `POST /{slug}/clients/create`
- `GET /{slug}/clients/edit/{id}`
- `POST /{slug}/clients/update/{id}`
- `GET /{slug}/clients/details/{id}`
- `GET /{slug}/clients/delete-confirm/{id}`
- `POST /{slug}/clients/delete/{id}` or `DELETE /{slug}/clients/{id}`

Optional later route:

- `GET /{slug}/clients/select-list`

## Service Responsibilities

- list filtering and pagination
- duplicate detection
- CRUD mapping
- details composition
- lightweight selection results for downstream modules

## UX Contract

### Index Page

- page title and create button
- filter/search bar
- list container that refreshes through HTMX
- empty state that prompts first client creation

### Form Modal

- inline validation errors
- clear required versus optional fields
- success closes modal and refreshes list

### Details View

- summary profile
- future relationship summary hooks for quotes and bookings

## Tests Required

### Unit Tests

- filtering logic
- duplicate email detection
- create/update trimming or normalization behavior
- delete behavior around related records if applicable

### Integration Tests

- full page render
- list partial render
- open create form
- valid create closes modal and refreshes list
- invalid create re-renders form with errors
- protected-route access control

### Manual QA

- search feels responsive
- modal lifecycle is clean
- empty state is clear
- details view remains readable on mobile and desktop

## Migration Notes

AgencyCardPro also used client selectors for quote builder integration. That selector can be added later once the Quotes module is being implemented.
