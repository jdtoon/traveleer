# Phase 2 - Inventory Module Spec

## Goal

Port AgencyCardPro inventory management into Traveleer as a foundational tenant module that later pricing, rate-card, and booking workflows can depend on.

## Source Reference

AgencyCardPro inventory module lives in:

- `c:/dev/agencycardpro/src/Modules/Inventory/Controllers/InventoryController.cs`
- `c:/dev/agencycardpro/src/Modules/Inventory/Services/InventoryService.cs`
- `c:/dev/agencycardpro/src/Modules/Inventory/Entities/InventoryItem.cs`
- `c:/dev/agencycardpro/src/Modules/Inventory/Views/*`

## Why It Is A Good Early Module

- it is foundational for later pricing and quote work
- it has moderate complexity but clear CRUD boundaries
- it lets Traveleer establish reusable list/filter/modal patterns before tackling rate cards

## Target Module Location

- `src/Modules/Inventory/`

Recommended structure:

- `InventoryModule.cs`
- `Controllers/InventoryController.cs`
- `Data/InventoryItemConfiguration.cs`
- `DTOs/InventoryItemDto.cs`
- `DTOs/InventoryFilterDto.cs`
- `Entities/InventoryItem.cs`
- `Events/InventoryEvents.cs`
- `Services/InventoryService.cs`
- `Views/Inventory/Index.cshtml`
- `Views/Inventory/_List.cshtml`
- `Views/Inventory/_Form.cshtml`
- `Views/Inventory/_DeleteConfirm.cshtml`

## Source Behavior Worth Preserving

- type filtering
- search by item or supplier
- pagination behavior
- create/edit/delete flows
- inventory categories or types

## Target Behavior In Traveleer

### Core CRUD

- list inventory for current tenant
- filter by type and search term
- create/edit/delete using HTMX modal flows
- future-ready fields for supplier and pricing integration

### UI Direction

AgencyCardPro split search, tabs, pagination, and grid into separate partials. Traveleer can simplify this if the user experience becomes cleaner, but it should keep modular HTMX refresh points.

## Data Notes

Inventory is tenant-owned and belongs in `TenantDbContext`.

Suggested fields:

- `Id`
- `Name`
- `Description`
- `SupplierName` or future supplier relation
- `BaseCost`
- `ImageUrl` or storage reference
- `Type`
- `Rating`
- audit fields

A later supplier module or relation can normalize supplier ownership more cleanly.

## Controller Surface

Suggested routes:

- `GET /{slug}/inventory`
- `GET /{slug}/inventory/list`
- `GET /{slug}/inventory/new`
- `POST /{slug}/inventory/create`
- `GET /{slug}/inventory/edit/{id}`
- `POST /{slug}/inventory/update/{id}`
- `GET /{slug}/inventory/delete-confirm/{id}`
- `POST /{slug}/inventory/delete/{id}` or `DELETE /{slug}/inventory/{id}`

Optional later partial routes:

- tabs/filter partials if needed by the final UX

## Service Responsibilities

- search and type filtering
- pagination
- create/update/delete mapping
- normalization and validation of enum/type input
- future integration points for suppliers and rate cards

## UX Contract

### Index Page

- clear title and create button
- search/filter controls above the list
- readable list or card layout depending on content density
- empty state that explains why inventory matters for later workflows

### Form Modal

- type selection
- supplier and cost fields
- validation and success feedback

## Tests Required

### Unit Tests

- type parsing and filtering
- page-size normalization
- create/update mapping
- delete behavior

### Integration Tests

- full page render
- list partial render
- filter/search interaction
- valid create and update flows
- invalid form behavior
- protected-route access control

### Manual QA

- filter changes feel smooth
- search results are coherent
- form layout remains usable on mobile
- empty states and success feedback are obvious

## Migration Notes

Inventory should be completed before heavy RateCards work begins because it is likely to be a prerequisite for pricing structures and supplier-related workflows.
