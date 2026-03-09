# Phase 2 - Onboarding Module Spec

## Goal

Design a post-registration onboarding experience that helps new tenants become productive quickly without replacing Traveleer's existing registration and provisioning flow.

## Source Reference

AgencyCardPro onboarding concepts live in:

- `c:/dev/agencycardpro/src/Modules/Onboarding/Controllers/OnboardingController.cs`
- `c:/dev/agencycardpro/src/Modules/Onboarding/Services/OnboardingWizardModel.cs`
- `c:/dev/agencycardpro/src/Modules/Onboarding/Views/*`

## Target Direction

Onboarding in Traveleer should begin after tenant registration and provisioning succeed.

The onboarding module should not become a public sign-up replacement. Instead, it should guide a newly provisioned tenant through first-use setup inside the tenant shell.

## Why Direct Porting Is Wrong

AgencyCardPro stores onboarding progress and setup assumptions inside a single-tenant app model. Traveleer must keep onboarding state isolated per tenant and aligned with its existing registration module.

## User Journey

1. User reaches Traveleer marketing pages.
2. User registers a tenant through `Registration`.
3. Tenant is provisioned through existing Traveleer infrastructure.
4. On first authenticated entry, the tenant is routed into onboarding if not completed.
5. After onboarding, the tenant lands on the app dashboard or first recommended workspace.

## Recommended Step Structure

### Step 1: Agency Identity

- confirm or refine tenant display name
- configure branding basics
- optionally upload logo

### Step 2: Business Settings

- set core travel-agency defaults
- choose baseline currency or operational preferences where required

### Step 3: Starter Data

- create first client or supplier/inventory item
- or choose a starter template path

### Step 4: Workflow Readiness

- guide user toward quotes, bookings, or inventory setup depending on chosen path

### Step 5: Completion

- summarize completed setup
- provide obvious next actions

## Data Ownership

Onboarding state should be tenant-owned.

Possible model:

- `TenantOnboardingState`

Suggested fields:

- `Id`
- `CompletedAt`
- `CurrentStep`
- `CompletedStepsJson` or normalized completion flags
- `SkippedAt`
- `Version`

## Module Placement

Recommended dedicated module:

- `src/Modules/Onboarding/`

Reasons:

- the workflow is bigger than one settings form
- it may orchestrate multiple downstream modules
- it deserves isolated tests and documentation

## HTTP Surface

Recommended routes:

- `GET /{slug}/onboarding`
- `GET /{slug}/onboarding/step/{step}`
- `POST /{slug}/onboarding/step/{step}`
- `POST /{slug}/onboarding/skip`
- `POST /{slug}/onboarding/complete`

## Service Layer

Recommended orchestration service:

- `GetStateAsync()`
- `GetStepViewModelAsync(step)`
- `SaveStepAsync(step, dto)`
- `CompleteAsync()`
- `ShouldRedirectToOnboardingAsync()`

This service should coordinate with future modules instead of duplicating their business logic.

## UX Contract

- multi-step progress indicator
- clear explanation for why each step matters
- save-and-continue behavior
- skip behavior only where safe
- helpful empty-state guidance at the end
- mobile-safe step layout

## Integration Points

- registration success path
- tenant dashboard entry routing
- branding module or tenant branding slice
- settings and seed/demo services

## Tests Required

### Unit Tests

- redirect decision logic
- step progression rules
- completion state handling
- skip rules

### Integration Tests

- onboarding shell renders for incomplete tenants
- a step partial or step page loads correctly
- valid step submission advances progress
- invalid step submission keeps user on the step with clear errors
- completed tenants no longer get redirected into onboarding

### Manual QA

- first-use journey feels guided rather than bureaucratic
- it is obvious what can be skipped and what cannot
- no dead-end step or stale modal/state is left behind

## Open Decisions

- mandatory versus skippable onboarding
- what minimum settings are required before exit
- whether starter data creation is manual, assisted, or automated
- whether discipline-specific onboarding variants ever exist later
