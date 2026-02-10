# SaaS Starter Kit — Documentation Index

> **A modular monolith SaaS starter kit built on .NET 10, Swap.Htmx, Tailwind CSS v4, DaisyUI 5, and SQLite with database-per-tenant architecture.**

This documentation suite is the **authoritative specification** for the SaaS starter kit. Every implementation decision flows from these docs. They are ordered by dependency — foundational architecture first, then domain modules, then integrations.

---

## 🏗️ Read This First

Start with **[01 — Architecture](01-architecture.md)** to understand the overall system structure, then follow the numbered order below. Each doc is self-contained but references others where dependencies exist.

---

## 📚 Documentation

| # | Document | Description |
|---|----------|-------------|
| 01 | [Architecture](01-architecture.md) | Modular monolith structure, project layout, database strategy (core + audit + N tenant DBs), Docker volume layout, environment layering |
| 02 | [Database & Multi-Tenancy](02-database-multitenancy.md) | Three DbContext types, entity schemas, EF Core migration separation, tenant resolution middleware, dynamic connection strings, tenant DB provisioning |
| 03 | [Modules](03-modules.md) | Module contract & conventions, registration pattern, shared interfaces, complete module inventory with responsibilities |
| 04 | [Authentication & Authorization](04-auth.md) | Magic link auth (super admin + tenants), ASP.NET Identity per tenant DB, RBAC (roles, permissions, tag helpers), auth cookie strategy |
| 05 | [Feature Flags](05-feature-flags.md) | Microsoft.FeatureManagement with custom DB-backed provider, plan-linked features, super admin overrides, tag helpers, controller gating |
| 06 | [Billing & Paystack](06-billing-paystack.md) | Plans, subscriptions, invoices, payments, Paystack integration, registration flow, webhook handling, local mock mode |
| 07 | [Infrastructure & DevOps](07-infrastructure.md) | Docker Compose, Litestream (dynamic tenant DB replication to R2), Cloudflare Turnstile, rate limiting, AWS SES, data protection, compression |
| 08 | [Local Development](08-local-development.md) | Clone-and-run guarantee, appsettings defaults, zero-config local setup, startup checklist |
| 09 | [Marketing Module](09-marketing.md) | Public-facing marketing site, landing page, pricing page, registration entry point, SEO |

---

## 🔗 Reference

| Resource | Link |
|----------|------|
| Swap.Htmx Docs | [swaphtmx.dev](https://swaphtmx.dev) |
| Swap.Htmx LLM Playbook | [swap.htmx.llms.txt](swap.htmx.llms.txt) |
| HTMX | [htmx.org](https://htmx.org) |
| DaisyUI 5 | [daisyui.com](https://daisyui.com) |
| Tailwind CSS v4 | [tailwindcss.com](https://tailwindcss.com) |
| Litestream | [litestream.io](https://litestream.io) |
| Paystack | [paystack.com](https://paystack.com) |

---

## 🧭 Technology Stack

| Layer | Technology |
|-------|-----------|
| **Runtime** | .NET 10 |
| **UI Orchestration** | Swap.Htmx (HTMX first-class in .NET) |
| **Styling** | Tailwind CSS v4 + DaisyUI 5 |
| **Database** | SQLite (WAL mode) — one core DB, one audit DB, one DB per tenant |
| **ORM** | Entity Framework Core |
| **Auth** | ASP.NET Identity + Magic Link (no passwords) |
| **Feature Flags** | Microsoft.FeatureManagement + custom DB provider |
| **Billing** | Paystack |
| **Email** | AWS SES (production) / Console (local) |
| **Backup/Replication** | Litestream → Cloudflare R2 |
| **Bot Protection** | Cloudflare Turnstile |
| **Containerization** | Docker + Docker Compose |
| **Client Packages** | LibMan (no Node.js required) |
| **Bundling** | LigerShark WebOptimizer |
