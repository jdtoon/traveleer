# Phase 1 - Branding And Theming Strategy

## Goal

Design a branding model that works for a multi-tenant SaaS product while preserving Traveleer's DaisyUI + Tailwind foundation.

## Current Reality

### In AgencyCardPro

- branding is effectively singleton per deployment
- theme values are served dynamically from one agency settings record
- public site and in-app branding are closely coupled

### In Traveleer

- public marketing layout already exists
- tenant layouts already exist
- DaisyUI and Tailwind are the destination UI system
- no tenant branding architecture exists yet

## Decisions Already Made

- branding is first-roadmap scope
- public platform branding and tenant branding are separate concerns
- the UI destination is DaisyUI + Tailwind, not imported custom CSS

## Target Model

### Platform Brand

Used for:

- `Marketing` module pages
- registration entry flow
- public trust and positioning pages

This remains controlled by the SaaS product owner and is not tenant-configurable in the initial roadmap.

### Tenant Brand

Used for:

- tenant shell and navigation chrome
- selected in-app accents and identity surfaces
- generated PDFs and vouchers
- email templates that should reflect tenant identity

## Recommended Architecture

### New Tenant-Scoped Settings Model

Add a tenant-owned branding/settings record with fields such as:

- display name
- logo asset path or storage key
- primary color
- secondary color
- accent color
- surface preference if needed later
- email header/footer presentation values
- document branding values for PDFs or vouchers

### Dynamic Theme Output

Create a tenant-aware CSS endpoint such as:

- `/{slug}/branding/theme.css`

This endpoint should:

- resolve tenant settings
- emit CSS custom properties only
- avoid replacing DaisyUI wholesale
- layer tenant variables on top of the chosen DaisyUI theme

### Layout Integration

Tenant layouts should reference the tenant branding stylesheet or inject tenant CSS variables into the page shell.

Recommended integration points:

- `src/Views/Shared/_TenantLayout.cshtml`
- tenant admin/settings surfaces where branding is managed
- PDF and email rendering services

## DaisyUI Strategy

Use DaisyUI as the base component system.

Do:

- keep one default theme for the platform
- override selected CSS variables for tenant identity
- keep component classes standard
- test contrast and readability across sample tenant palettes

Do not:

- fork the entire theme system per tenant
- port AgencyCardPro's CSS files into Traveleer
- allow uncontrolled branding values that break accessibility

## Implementation Notes

A future Branding module or tenant settings extension should cover:

- tenant branding edit form
- preview of brand colors in app context
- logo management
- validation and normalization of color input
- fallback defaults when tenant branding is incomplete

## Open Questions For Later Spec

- should branding live in a dedicated `Branding` module or as part of a broader tenant settings module?
- should PDFs and email templates share one brand contract or separate contracts?
- what storage mechanism should own logos and brand assets?
- how much of the tenant shell should be brand-aware versus neutral?
