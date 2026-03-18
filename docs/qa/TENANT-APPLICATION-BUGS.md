# Tenant Application Bug Log

## Purpose

This document tracks confirmed tenant-application defects discovered during manual browser QA and SQLite-backed verification.

## Session Metadata

- Date: 2026-03-17
- Tester: GitHub Copilot
- Environment: Local development seed data
- Base URL: http://localhost:5000
- Tenant slug: demo

## Status Legend

- `Open` - confirmed and not yet fixed
- `Retest needed` - suspected fixed, pending QA confirmation
- `Closed` - fix verified

---

## BUG-001: Tasks create validation crashes on blank title

- Status: Closed
- Severity: High
- Module: Tasks
- Route: `/demo/tasks/create`
- Persona: Tenant member or admin with task-create permission

### Reproduction Steps

1. Open `/demo/tasks`.
2. Click `+ New Task`.
3. Submit the create request with a blank `Title` and a valid antiforgery token.

### Expected Result

- The modal should remain open.
- The form should show inline validation for the required title.
- The response should return a normal HTMX validation payload without crashing.

### Actual Result

- The request returns `500 Internal Server Error`.
- The response exposes a raw server stack trace.
- The root persistence failure is `SQLite Error 19: 'NOT NULL constraint failed: AgentTasks.Title'`.

### Evidence

- Browser QA reproduced the issue on 2026-03-17.
- The invalid request did not insert a row into `AgentTasks`, but it failed with a server exception instead of validation handling.
- Stack trace terminates through:
  - `saas.Modules.Tasks.Services.TaskService.CreateAsync(...):line 98`
  - `saas.Modules.Tasks.Controllers.TaskController.Create(...):line 73`
- Fix verified on 2026-03-18: blank and whitespace-only title submissions now return the task modal with inline validation text `The Title field is required.` and SQLite task count remains unchanged.

### Impact

- Users receive a crash instead of recoverable form feedback.
- The response leaks internal implementation details in the browser.
- This fails the tenant UX validation standard for invalid form handling.

### Recommended Fix Direction

- Add server-side `ModelState` handling in the Tasks create flow before persistence.
- Return the task modal with inline validation and an error toast instead of allowing the DB constraint exception to surface.

### Resolution

- Added data-annotation validation to the task DTOs.
- Added controller-side invalid-model handling to re-render the modal with validation feedback before persistence.
- Added targeted integration coverage for invalid task creation.

---

## BUG-002: Portal link revoke fails with `htmx:targetError`

- Status: Retest needed
- Severity: High
- Module: Portal
- Route: `/demo/portal/links` and `/demo/portal/links/revoke/{id}`
- Persona: Tenant admin with portal-management permission

### Reproduction Steps

1. Open `/demo/portal/links`.
2. Click `Revoke` on an active portal link.

### Expected Result

- The portal link should be marked revoked.
- The list should refresh in place without client-side errors.
- SQLite should show `IsRevoked = 1` for the targeted link.

### Actual Result

- The browser console reports `htmx:targetError` when the revoke button is used.
- The list does not refresh correctly.
- The targeted portal link remains active in SQLite.

### Evidence

- Browser QA reproduced the issue on 2026-03-17 from the real portal-links page.
- SQLite verification after the UI action confirmed `IsRevoked` remained `0` for QA portal link `28B697B3-F591-4F0C-A5BA-797DB05776E4`.
- Clean-host retest on 2026-03-18 at `http://localhost:5100` did not reproduce the issue: the same portal link updated to `Revoked` in the UI, SQLite persisted `IsRevoked = 1`, and the audit DB recorded an `Updated|PortalLink` entry.

### Impact

- Original repro indicates tenant admins may not be able to revoke active client portal access under some runtime conditions.
- The issue could be runtime- or environment-specific, so it still needs confirmation on the next stable retest before being closed.

### Recommended Fix Direction

- Correct the HTMX revoke response/target contract for `#portal-link-list`.
- Ensure the revoke action persists `IsRevoked = 1` and refreshes the visible list without target errors.
- Compare the originally failing host/runtime against the clean `5100` retest to identify whether stale assets, partial reload state, or a host-specific mismatch caused the original failure.

---

## BUG-003: Public portal pages reference missing Swap.Htmx script asset

- Status: Closed
- Severity: Medium
- Module: Portal
- Route: Public portal pages such as `/portal/{tenantSlug}/{token}/dashboard` and `/portal/{tenantSlug}/{token}/quotes`
- Persona: Portal client user

### Reproduction Steps

1. Open a valid public portal link.
2. Load the portal dashboard or quotes page.
3. Inspect browser console/runtime events.

### Expected Result

- Required frontend assets should load successfully.
- The page should not emit missing-script or MIME-type execution errors.

### Actual Result

- The browser requests `/_content/Swap.Htmx/js/swap.js` and receives `404 Not Found`.
- The browser then logs a MIME-type refusal because the returned content is HTML rather than JavaScript.

### Evidence

- Reproduced on portal dashboard and quotes pages during QA on 2026-03-17.
- Console/runtime output included:
  - `Failed to load resource: the server responded with a status of 404 (Not Found)`
  - `Refused to execute script ... because its MIME type ('text/html') is not executable`
- Fix verified on 2026-03-18: public portal pages now reference `/_content/Swap.Htmx/js/swap.client.js`, targeted integration coverage passed, and clean-host browser retest no longer emitted the missing-script runtime error.

### Impact

- Public portal pages ship with a repeatable frontend runtime error.
- Even when core page content renders, this indicates broken asset wiring and increases risk for client-side regressions.

### Recommended Fix Direction

- Correct the public portal asset reference for Swap.Htmx or remove the script include if it is not required on these pages.
- Verify public portal pages load without console/runtime asset errors.

### Resolution

- Updated the public portal layout to use `swap.client.js` instead of the non-existent `swap.js` asset.
- Updated the shared payment public layout to the same correct asset path to keep public-facing HTMX surfaces consistent.

---

## BUG-004: Branding save returns 404 and never persists changes

- Status: Retest needed
- Severity: High
- Module: Branding
- Route: `/demo/branding/save`
- Persona: Tenant admin or any user who can see the Branding save form

### Reproduction Steps

1. Open `/demo/branding`.
2. Change one or more editable branding fields such as `Website`, `ContactPhone`, or `Quote footer note`.
3. Submit the `Save Branding` form.

### Expected Result

- Valid data should save successfully and refresh the branding page.
- Invalid data should return the form with inline validation errors.
- Successful saves should persist to `BrandingSettings` and emit an audit entry.

### Actual Result

- The request to `/demo/branding/save` returns `404 Not Found`.
- The page remains on `/demo/branding` with unsaved values still in the browser inputs.
- SQLite shows no change in `BrandingSettings`.
- No audit entry is written for the attempted change.

### Evidence

- Reproduced on 2026-03-17 with both invalid and valid submissions.
- Tenant DB row remained unchanged after each attempt.
- Audit DB count for tenant `demo` remained unchanged during branding-save attempts.
- Clean-host retest on 2026-03-18 at `http://localhost:5100` did not reproduce the issue: a real browser submit returned `Branding updated.`, the preview updated, and the audit DB recorded `Updated|BrandingSettings|admin@demo.local` entries.

### Impact

- Original repro indicates branding save may fail under some runtime conditions.
- The clean-host retest passed without requiring a Branding code change, so the issue appears environment- or runtime-specific until proven otherwise.

### Recommended Fix Direction

- Fix the `/demo/branding/save` authorization or route handling so it accepts valid submissions from users who can access the branding editor.
- If the current user should not be allowed to edit branding, hide or disable the save UI consistently instead of exposing a broken action.
- Compare the original `5000`-host failure against the successful `5100` retest before closing this item.
