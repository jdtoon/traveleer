# Phase 1 - Public Site And Onboarding

## Goal

Align Traveleer's public-facing experience with the eventual travel product while preserving the existing multi-tenant registration foundation.

## Public Site Direction

### Current Position

- Traveleer already has a `Marketing` module with landing, pricing, contact, about, terms, and privacy pages.
- AgencyCardPro has travel-oriented public content in `business-site/`.

### Recommended Direction

- keep Traveleer's marketing module as the rendering destination
- migrate content, structure, and messaging from AgencyCardPro into that module
- redesign the pages in Traveleer's existing DaisyUI/Tailwind language instead of importing static HTML and CSS directly

### Public Work To Plan

- homepage value proposition and section structure
- travel-specific pricing and positioning
- FAQ and trust sections
- terms/privacy copy alignment
- visual direction for a stronger travel identity

## Registration And Onboarding Direction

### Current Position

- Traveleer already supports public registration and tenant provisioning
- AgencyCardPro's onboarding is a post-login setup flow in a single-tenant app

### Recommended Direction

Treat onboarding as a post-registration tenant experience, not as a replacement for Traveleer's registration system.

### Proposed Lifecycle

1. user lands on marketing page
2. user registers a new tenant through Traveleer registration
3. tenant is provisioned
4. first-login or first-entry flow guides the tenant through onboarding

### Candidate Onboarding Steps

- confirm agency identity and display name
- upload logo or define branding basics
- configure default business settings
- optionally seed starter data or templates
- land the user in the tenant dashboard with clear next actions

## Architecture Constraints

- onboarding must be tenant-aware
- onboarding progress cannot be global or deployment-wide
- the flow should use Traveleer's layouts and HTMX patterns
- complex setup should still feel guided and lightweight

## Deferred Decision Areas

- should onboarding be mandatory or skippable?
- should onboarding live in a dedicated module or as a guided settings flow?
- what starter data is worth generating automatically?
- which onboarding choices are reversible later in settings?

## Outcome

This workstream ensures the product feels coherent from first visit through first usable tenant session, instead of treating marketing, signup, and tenant setup as disconnected systems.
