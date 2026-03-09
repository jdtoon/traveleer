# Phase 1 - Instruction Adoption

## Purpose

Bring the ClinicDiary engineering discipline into Traveleer without breaking Traveleer's own contracts.

## Source Of Truth

- `c:/dev/clinicdiary/.github/instructions/development.instructions.md`
- `c:/dev/clinicdiary/.github/instructions/testing.instructions.md`

## Destination

- `c:/dev/traveleer/.github/instructions/development.instructions.md`
- `c:/dev/traveleer/.github/instructions/testing.instructions.md`

## What Must Be Carried Over

### Development Discipline

- hard completion gate using build + unit + integration tests
- UX-first rule for every feature
- modular architecture requirements
- explicit list of forbidden shortcuts
- clear controller, service, entity, and view conventions
- no inline styles and no UI drift away from DaisyUI + Tailwind

### Testing Discipline

- full-page tests
- HTMX partial-isolation tests
- end-to-end user-flow tests
- valid and invalid form coverage
- manual Playwright QA for changed UX
- regression verification after feature completion

## Traveleer-Specific Adaptations

| ClinicDiary Pattern | Traveleer Adaptation |
|--------------------|----------------------|
| `clinicdiary.csproj` build command | `saas.csproj` build command |
| ClinicDiary module examples | Traveleer `IModule` contract from `src/Shared/IModule.cs` |
| Reference to healthcare modules | References to travel-domain modules |
| Strict DaisyUI rules | Same rule, but tuned to Traveleer's layouts and public/tenant split |
| Three-layer integration tests | Same pattern using `tests/saas.IntegrationTests` |

## Immediate Result

Traveleer now has a written operating model for the upcoming migration. This reduces ambiguity before domain modules are added.

## Follow-On Work

- keep the new instruction files current as the first app modules land
- add module-specific conventions later only if repeated patterns emerge
- avoid duplicating ADR content inside the instructions
