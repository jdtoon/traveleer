# Phase 1 - Data Migration Strategy

## Goal

Define how AgencyCardPro data and domain concepts will move into Traveleer's multi-database, multi-tenant architecture.

## Core Rule

Do not move single-tenant assumptions into Traveleer unchanged.

## Database Ownership

| Concern | Target Context | Notes |
|---------|----------------|-------|
| Tenants, plans, subscriptions, feature flags | `CoreDbContext` | Existing Traveleer platform ownership |
| Travel-domain operational data | `TenantDbContext` | Clients, inventory, rates, quotes, bookings, settings |
| Audit trail | `AuditDbContext` | Existing audit pipeline |

## Candidate Tenant-Owned Domains

These should normally live in tenant storage:

- clients and contacts
- inventory and suppliers
- rate cards and templates
- quotes and quote versions
- bookings and booking items
- branding/settings related to a single tenant
- onboarding progress for a tenant

## Migration Principles

1. Each AgencyCardPro entity must be classified by ownership before implementation.
2. Entity configuration must be created in the owning module's `Data/` folder.
3. New tenant entities must be seeded only when it adds value for provisioning or demo scenarios.
4. Numbering schemes must be tenant-safe and not depend on one-deployment assumptions.
5. Settings records must be tenant-scoped by default unless there is a clear platform-wide reason not to.

## Domain Translation Notes

### Branding And Settings

AgencyCardPro singleton settings become tenant-scoped settings in Traveleer.

### Onboarding State

Any onboarding completion state must be stored in a tenant-aware way so that each tenant progresses independently.

### Quotes And Bookings

These workflows should move into tenant-owned modules with clear entity boundaries and auditable state transitions.

### Rate Cards And Pricing

Import structures and templates likely remain tenant-owned because agencies can vary independently.

## Seeder Direction

A later seed pass should create coherent demo travel data for the `demo` tenant, for example:

- sample clients
- sample suppliers or inventory items
- sample rate cards
- sample quotes
- sample bookings and document outputs where appropriate

The seed data must support real browser QA and integration tests, not just basic list rendering.

## Migration Deliverables Needed Later

- entity-by-entity ownership map
- numbering strategy per module
- import/export contract notes
- PDF and email data requirements
- seed/demo dataset plan

## Non-Goals In This Phase

This document does not define final entities yet. It defines the rules that those future specs must follow.
