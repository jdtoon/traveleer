# 08 — Styling and Layout

## Current State

The application uses:
- **Tailwind CSS v4** via `input.css`
- **DaisyUI v5** with themes: `lemonade` (tenant), `nord` (admin), `abyss` (super-admin), `light` (public)
- Dynamic tenant branding via CSS custom properties loaded through HTMX (`_ThemeVars.cshtml`)
- Custom CSS for `swap-nav`, `[data-loading]`, modal backdrop blur, and mobile touch targets

---

## SL-1. Branding Theme Variables Load After First Paint (FOUC)

**Priority: P1**

The tenant branding CSS variables are loaded via HTMX:
```html
<div id="branding-theme-vars"
     hx-get="@Url.Action("ThemeVars", "Branding", new { slug })"
     hx-trigger="load, branding.refresh from:body"
     hx-swap="outerHTML">
</div>
```

This means on first page load:
1. HTML renders with default `lemonade` theme colors
2. HTMX fires GET request for theme variables
3. CSS custom properties are injected
4. Sidebar gradient and primary/secondary colors change

Users see a brief flash (FOUC) — the sidebar gradient and accent colors pop in after the page is already visible.

**Fix**: Server-render the theme variables in the initial layout response instead of lazy-loading:

```html
@{
    var theme = await BrandingService.GetThemeAsync();
}
<style id="branding-theme-vars">
    .tenant-brand-surface {
        --color-primary: @theme.PrimaryColor;
        ...
    }
</style>
```

Keep the HTMX trigger for `branding.refresh` events (when user saves new branding), but render initial values inline to prevent FOUC.

---

## SL-2. Brand Shell (Logo/Name) Also Causes Flash

**Priority: P1**

Same issue as above for the brand shell in the sidebar:
```html
<div id="tenant-brand-shell"
     hx-get="@Url.Action("Shell", "Branding", new { slug })"
     hx-trigger="load, branding.refresh from:body"
     hx-swap="innerHTML">
    <span class="text-xl font-bold">@(TenantContext.TenantName ?? "saas")</span>
</div>
```

The fallback text ("saas" or tenant name) renders first, then the branded shell with logo replaces it.

**Fix**: Server-render the initial shell content. The HTMX endpoint can still be called to refresh after branding changes, but the initial render should already include the logo and branded name.

---

## SL-3. Mobile Drawer Close on Navigation Is JavaScript-Only

**Priority: P2**

Sidebar items close the mobile drawer with:
```html
onclick="document.getElementById('mobile-drawer').checked=false"
```

This works for click events but doesn't close the drawer when Swap.Htmx processes a `swap-nav` navigation programmatically. If navigation is triggered by browser history (back/forward), the drawer stays open.

**Fix**: Add an HTMX after-swap handler that closes the drawer:

```javascript
document.addEventListener('htmx:afterSwap', function(evt) {
    if (evt.detail.target.id === 'main-content') {
        document.getElementById('mobile-drawer').checked = false;
    }
});
```

---

## SL-4. Rate Card Grid Horizontal Scroll Needs Better Affordance

**Priority: P2**

The rate card grid (`_Grid.cshtml`) uses `min-w-225` which overflows on screens less than ~900px. The table is wrapped in `overflow-x-auto`, but there's no visual indicator that horizontal scrolling is needed.

**Fix**: Add a gradient fade on the right edge when content overflows:

```css
.rate-grid-wrapper {
    position: relative;
}
.rate-grid-wrapper::after {
    content: '';
    position: absolute;
    right: 0; top: 0; bottom: 0; width: 2rem;
    background: linear-gradient(to right, transparent, var(--b1));
    pointer-events: none;
    opacity: 0;
    transition: opacity 0.2s;
}
.rate-grid-wrapper.has-scroll::after {
    opacity: 1;
}
```

Detect overflow with a small JS snippet that adds `has-scroll` class.

---

## SL-5. Inventory Card Grid Breaks at Certain Widths

**Priority: P2**

Inventory uses a card grid: `grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3`. Between `md` and `xl` breakpoints (768px–1280px), cards are shown in 2 columns. But the sidebar takes ~256px (16rem), leaving only ~512px–1024px for 2 columns. At the lower end, cards may appear cramped.

**Fix**: Consider `lg:grid-cols-2 xl:grid-cols-3` breakpoints (since the sidebar already consumes the `md` space), or switch to a responsive auto-fill grid:

```css
grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
```

---

## SL-6. Modal Width Inconsistency

**Priority: P3**

Different modules use different modal widths:

| Module | Modal | Width Class |
|--------|-------|-------------|
| Clients | Form | `max-w-lg` |
| Clients | Details | `max-w-2xl` |
| Bookings | Form | `max-w-3xl` |
| Suppliers | Form | `max-w-2xl` |
| Itineraries | Form | Needs check |
| Rate Cards | Season Form | `max-w-lg` |
| Settings | Entity Forms | `max-w-lg` |

This isn't necessarily wrong (forms with more fields need more space), but verify that the chosen widths are appropriate for the content in each modal. A simple create form shouldn't use `max-w-3xl`.

---

## SL-7. No Print Stylesheet

**Priority: P3**

The application has no `@media print` styles. Users may want to print:
- Booking details (for file)
- Quote preview (for PDF alternative)
- Rate card grid (for supplier reference)
- Reports widgets

**Fix**: Add a print stylesheet that:
- Hides the sidebar, navbar, toast container, modal container
- Shows `#main-content` at full width
- Optimizes table layouts for paper
- Hides interactive elements (buttons, forms)

```css
@media print {
    .drawer-side, .navbar, #toast-container, #modal-container { display: none; }
    .drawer-content { margin-left: 0; }
    #main-content { padding: 0; }
    .btn, form { display: none; }
}
```

---

## SL-8. Color Contrast in Theme Variables Not Validated

**Priority: P2**

The branding module auto-calculates readable text colors using luminance:

```csharp
private static string GetReadableTextColor(string hexColor)
{
    var luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
    return luminance > 160 ? "#0F172A" : "#FFFFFF";
}
```

The threshold of 160 may not meet WCAG AA contrast ratio (4.5:1) for all colors. Mid-range grays (luminance ~150–170) may produce insufficient contrast.

**Fix**: Use the actual WCAG contrast ratio formula instead of a simple luminance threshold:

```csharp
private static double GetContrastRatio(double l1, double l2)
{
    var lighter = Math.Max(l1, l2);
    var darker = Math.Min(l1, l2);
    return (lighter + 0.05) / (darker + 0.05);
}
```

Ensure all text-on-background combinations achieve at least 4.5:1 contrast.

---

## SL-9. Skip Navigation Link Present but May Not Be Styled

**Priority: P3**

The layout includes `<a href="#main-content" class="skip-branding sr-only ...">Skip to main content</a>`. Verify this link:
1. Is visible on keyboard focus (`:focus:not-sr-only`)
2. Actually scrolls to `#main-content`
3. Uses high-contrast colors when visible

The existing classes suggest it's handled but should be tested with keyboard navigation.
