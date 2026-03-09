# Phase 1 - Testing And QA Rollout

## Goal

Make testing and manual QA a built-in part of the migration instead of something added after modules are ported.

## Baseline Rule

Every feature migration must satisfy:

- `dotnet build src/saas.csproj`
- `dotnet test tests/saas.UnitTests`
- `dotnet test tests/saas.IntegrationTests`
- manual Playwright QA on changed user-facing flows

## Test Layers Required Per User-Facing Feature

### Layer 1 - Full Page

Confirms:

- layout renders
- the page shell includes expected HTMX targets
- navigation container and modal container exist where needed

### Layer 2 - Partial Isolation

Confirms:

- HTMX endpoints return partial content without layout
- core visible elements exist
- empty and filtered states behave correctly

### Layer 3 - User Flow

Confirms:

- page load plus HTMX interactions work together
- modal lifecycle is correct
- submit or state-change interactions emit the right updates

## Unit Test Emphasis

Service tests should lead on:

- pricing and calculations
- numbering generation
- validation and normalization
- quote and booking state transitions
- import parsing and error handling
- branding/settings validation

## Manual Browser QA Emphasis

For migrated features, manually verify:

- visual quality on desktop and mobile
- in-app navigation flow using real UI controls
- HTMX refresh behavior and absence of stale UI
- inline validation UX
- success and error feedback
- tenant-scoped correctness
- no raw exceptions or broken states

## Migration-Specific Regression Areas

At minimum, recheck:

- public marketing entry points touched by changes
- registration and post-registration flow if affected
- tenant dashboard entry and sidebar navigation
- any shared modal, toast, list, or refresh target used by the feature
- branding effects on layouts, emails, or documents where applicable

## Rollout Sequence

1. instructions in place
2. first module spec written
3. service tests written with module implementation
4. integration tests added before feature is considered complete
5. manual QA performed after automated tests pass
6. regression sweep before closing the work item

## Success Condition

The migration only counts as progress when the new Traveleer behavior is both correct and pleasant to use.
