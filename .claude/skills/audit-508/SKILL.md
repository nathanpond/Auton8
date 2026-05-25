---
name: audit-508
description: Codebase-wide Section 508 / WCAG 2.0 AA accessibility audit for AutoNate. Checks the SPA (Mantine v9 + React 19) and server-rendered surfaces for missing alt text, icon-only buttons without accessible names, keyboard traps, focus management in modals/menus, heading hierarchy, form-label association, color-contrast risk in `SiteAppearance`, ARIA landmarks, live-region usage on toasts/loading, `mantine-datatable` semantics, and `prefers-reduced-motion` honoring. Produces a verified punch list with severity. Invoked by `/audit 508`; can also be invoked directly. Use whenever the user asks about 508, accessibility, a11y, WCAG, screen-reader behavior, or keyboard navigation across the app.
---

# Section 508 accessibility audit (whole codebase)

A focused pass over every user-facing surface in AutoNate for compliance with Section 508 of the Rehabilitation Act. Section 508 (2018 refresh) incorporates **WCAG 2.0 Level AA by reference**, so the practical checklist below is WCAG-shaped — but framed in terms of what an external 508 assessor would flag on this codebase specifically.

**Scope**: the SPA at `src/AutoNate.Spa/src/`, any server-rendered HTML under `src/AutoNate.Server/`, plugin-contributed UI under `plugins/<name>/spa/`, and shared chrome (header, sidebar, modals, toasts). Out-of-scope items are listed at the end.

**Why this audit is its own thing**: accessibility regressions are easy to introduce silently — the page still renders, tests still pass, and only a screen-reader or keyboard user notices the breakage. A periodic codebase-wide pass catches the drift that PR-by-PR review tends to miss.

## What "508 compliant" means here

Section 508 maps to WCAG 2.0 AA. The audit is structured around the four WCAG principles (Perceivable, Operable, Understandable, Robust — "POUR") plus AutoNate-specific concerns the framework can't enforce automatically (custom theming, plugin-contributed UI, the chatbot sidebar).

Mantine v9 ships with a strong a11y baseline (focus trap, escape handling, ARIA wiring on `Modal`/`Menu`/`Combobox`/etc.), so the audit's job isn't to re-check the framework — it's to catch the places where AutoNate code **escapes** that baseline: raw `<div onClick>`, FontAwesome icon-only `<i>` glyphs without an accessible name, custom widgets that don't forward keyboard events, dynamic content that doesn't announce, and the `SiteAppearance` theming layer (admin-configurable, so a tenant can produce a low-contrast theme).

## Strategy

Spin up parallel `Explore` agents, one per concern. Then **verify each finding by reading the cited file and, where possible, by running an automated tool** before listing it in the report. Grep alone gives too many false positives for accessibility — e.g. an `<i className="fa fa-...">` inside a `<Button>` that has its own label is fine, but grep can't tell.

**Tooling note**: AutoNate does not currently ship `axe-core`, `@axe-core/react`, or `eslint-plugin-jsx-a11y` (verified by `grep "axe\|jsx-a11y" src/AutoNate.Spa/package.json`). Recommending one of these is itself a high-value audit finding under concern **K**. If `axe-core` is installed at the time of the audit, run it against the dev server and merge its findings into the report.

## Concerns to cover (one Explore agent per concern unless noted)

### A. Text alternatives for non-text content (WCAG 1.1.1)

- Every `<img>` has an `alt` attribute. Decorative images use `alt=""` (empty, not missing).
- Every FontAwesome icon-only control has an accessible name. The dominant pattern in this repo is:
  - `<i className="fa fa-...">` inside `<Button>` / `<ActionIcon>` — accessible only if the parent has `aria-label`, visible text, or `title`.
  - `<i>` used standalone as a "button" via `onClick` — almost always wrong; should be `<ActionIcon>` or `<Button>` with `aria-label`.
- `SVG` icons: have `<title>` / `aria-label` if conveying meaning, or `aria-hidden="true"` + `role="presentation"` if decorative.
- `SiteBrand.tsx` and `IconPicker.tsx` are known users of FA glyphs — review carefully.
- Detection: `grep -rn '<i className="fa' src/AutoNate.Spa/src/`, then for each hit, inspect the surrounding element to confirm there's an accessible name reachable to AT.

### B. Color, contrast, and color-only meaning (WCAG 1.4.1, 1.4.3, 1.4.11)

- Text contrast meets **4.5:1** for normal text, **3:1** for large text (≥18pt or ≥14pt bold). Focus indicators meet **3:1** against adjacent colors.
- `SiteAppearance` (the admin-configurable theme) is the highest-leverage finding source. `applySiteAppearanceToDocument` in `src/AutoNate.Spa/src/lib/siteAppearance.ts` writes the `--mantine-*` and `--app-*` vars from admin input. The audit checks:
  - Whether the configurator validates contrast (prevents an admin from producing a non-compliant theme).
  - Whether the inferred `data-mantine-color-scheme` (from `surfaceBg` luminance) actually picks the right scheme — a near-mid-luminance surface can flip light/dark incorrectly and crash contrast.
  - Whether `--app-header-fg` / `--app-header-bg` and `--app-sidebar-*` pairings have a documented contrast floor.
- Status indicators (status pills, `.row-archived`, `.notification-unread` in `widgets.css`): color is **not the sole signal** — there must also be a glyph, label, or position cue.
- Verification: pick the 3–5 highest-traffic palette combinations from `siteAppearance.ts` defaults and compute contrast ratios. Anything below the threshold is a high-severity finding.

### C. Keyboard accessibility (WCAG 2.1.1, 2.1.2, 2.4.3, 2.4.7)

- Every interactive element is reachable via Tab. No keyboard traps (`Tab` always escapes, except inside an active modal where the trap is intentional).
- Visible focus indicator on every focusable element. Mantine provides one by default — flag any `outline: none` or `:focus { ... }` rule that removes it without a replacement.
- Tab order matches visual order (no rogue `tabIndex={1}` etc.; `tabIndex={0}` and `tabIndex={-1}` are the only acceptable values).
- Skip-to-content link in `AppShell.tsx` so keyboard users can bypass the nav.
- Custom widgets in `src/AutoNate.Spa/src/widgets/` that wrap native semantics (anything not built on a Mantine primitive) implement the right key handlers: `Enter`/`Space` for buttons, arrow keys for listboxes/menus, `Escape` for dismissable surfaces.
- The `AgentSidebar` chatbot (per CLAUDE.md, `position: fixed`) needs first-class keyboard support — opening, closing, focusing the input, returning focus on close.
- Detection: `grep -rn "onClick" src/AutoNate.Spa/src/ | grep -E '<(div|span|li|td) '` — anything that's not a Mantine primitive or a real `<button>` is a finding candidate.

### D. Focus management for dynamic UI (WCAG 2.4.3, 2.4.11)

- **Modals**: focus moves into the modal on open, traps inside, returns to the trigger on close. Mantine's `<Modal>` does this; flag any non-Mantine dialog implementation. `src/AutoNate.Spa/src/components/ConfirmModal.tsx` is the canonical pattern.
- **Menus / dropdowns / comboboxes**: built on Mantine primitives (verify via `mantine-combobox` skill).
- **Route changes**: after client-side navigation, focus should move to the new page's `<h1>` or main landmark — otherwise screen-reader users are stranded mid-DOM. Check `router.tsx` and `ProtectedRoute.tsx` for any focus-management hook.
- **Toasts / notifications**: don't steal focus; instead, use `aria-live` (see concern **G**).

### E. Forms (WCAG 1.3.1, 3.3.1, 3.3.2, 3.3.3, 4.1.2)

- Every form input has a programmatic label. Mantine's `TextInput`/`Select`/etc. accept a `label` prop that wires `htmlFor` correctly — flag any raw `<input>` without an `<label>` or `aria-label`.
- Required fields are indicated programmatically (`required` attr or `aria-required="true"`), not just with a visual asterisk.
- Validation errors are announced. `@mantine/form` + `mantine-form-zod-resolver` is the standard stack; the audit checks that error messages render via Mantine's `error` prop (which wires `aria-describedby` / `aria-invalid`) rather than a stray `<Text c="red">` below the field.
- Groups of related controls (radio groups, checkbox groups, repeating fieldsets) use `<fieldset>` / `<legend>` or Mantine's `Group` with an associated label.
- Login form (`/account/login`) is the highest-priority finding location — most-trafficked form, also pre-auth.

### F. Page structure, semantics, and landmarks (WCAG 1.3.1, 2.4.1, 2.4.2, 2.4.6, 2.4.10)

- One `<h1>` per page; heading levels don't skip (`h1` → `h3` is a finding).
- Page `<title>` updates per route. `index.html` ships with `<title>AutoNate</title>`; the SPA must override per route — check `router.tsx` for a `useDocumentTitle` hook or `Helmet`-style mechanism.
- `<html lang="en">` is set (verified in `index.html`). If user-language preference exists, it propagates here.
- Landmark regions on the shell: `<header>` (or `role="banner"`), `<nav>`, `<main>`, `<aside>` (chatbot), `<footer>` if present. Mantine `AppShell` provides these by default — flag any custom shell that doesn't.
- Lists of items use `<ul>` / `<ol>` (and Mantine's `List`), not styled `<div>`s.

### G. Dynamic content announcements (WCAG 4.1.3)

- Toast notifications use `role="status"` or `aria-live="polite"` so screen readers announce them without stealing focus. Mantine `notifications` does this — verify the actual call sites use it (vs. a custom toast).
- Loading states announce. A spinner with no label is invisible to AT; pair with `<VisuallyHidden>Loading…</VisuallyHidden>` or `aria-busy` on the container.
- Validation errors that appear inline below a field are announced via `aria-describedby` (Mantine handles this via the `error` prop).
- Real-time updates (chatbot streaming responses, workflow execution status) use `aria-live` regions so the screen reader knows new content arrived. Especially relevant for `AgentSidebar`.

### H. Data tables (WCAG 1.3.1)

- The wrapper at `src/AutoNate.Spa/src/components/data-table/DataTable.tsx` ultimately renders `mantine-datatable`. Verify:
  - Column headers render as `<th>` (not `<td>`).
  - Sort state is announced — `aria-sort="ascending|descending|none"` on the header.
  - Table has an accessible name (`<caption>` or `aria-label`).
  - Empty / loading / error states are announced (concern G overlap).
- If the codebase has any HTML `<table>` outside the wrapper, those need scope attrs and a caption.

### I. Motion, animation, and timing (WCAG 2.2.1, 2.2.2, 2.3.1, 2.3.3)

- `prefers-reduced-motion` is honored. Look for CSS animations / transitions in `widgets.css`, `shell.css`, page-level styles, and any `motion`/`framer-motion` usage — they should be wrapped in `@media (prefers-reduced-motion: reduce)` overrides.
- No content flashes more than 3 times per second (WCAG 2.3.1). Unlikely in this codebase, but check any spinner or pulse animation.
- Auto-dismissing toasts give the user enough time to read (Mantine defaults are usually OK, but flag any `autoClose` value below ~5s).
- Session timeout warnings (if any): user can extend before forced logout.

### J. Plugin-contributed UI

- Plugins under `plugins/<name>/spa/` ship their own React components. The host can't enforce a11y in plugin code at compile time — but the audit should:
  - Sample 2–3 plugin SPA files and apply concerns **A**, **C**, **E**, **F** to them.
  - Check whether the plugin-creator skill (`.claude/skills/plugin-creator/SKILL.md`) mentions accessibility requirements; if not, that's itself a finding (drift between docs and standard).
- Plugin-contributed page templates registered via `IPluginContext.PageTemplates` flow through the same Mantine shell, so chrome a11y is inherited — but the page body is the plugin's responsibility.

### K. Tooling and process

- **`eslint-plugin-jsx-a11y`** is not in `src/AutoNate.Spa/package.json` (verify at audit time). Recommend adding it — most of concerns A, C, E surface as ESLint errors when this plugin is enabled.
- **`axe-core` / `@axe-core/react`** isn't installed either. Recommend `@axe-core/react` for dev-mode runtime a11y warnings.
- **Storybook / component sandbox** with axe addon — if the project gains a Storybook later, the addon catches widget-level issues before integration.
- **CI a11y check**: a Playwright + `@axe-core/playwright` script that drives the golden-path pages and fails CI on new violations. AutoNate already uses Playwright MCP for verification flows; the same setup can host a11y assertions.
- Document the chosen a11y baseline (WCAG 2.0 AA / 2.1 AA / 2.2 AA) in CLAUDE.md or README so future contributors know the target.

## Verification before reporting

Accessibility is the audit category most prone to false positives from a bare grep. For every finding:

1. **Read the cited file at the cited line.** Confirm the issue is real — e.g. an `<i>` glyph inside a `<Button>` whose own label says "Edit" is not a finding.
2. **Where possible, run an automated check.** If `axe-core` or `eslint-plugin-jsx-a11y` is available, run it and use its output to confirm the finding. If not, the recommendation to install it is concern K.
3. **For contrast findings**, compute the actual ratio (use a known formula or a local tool — don't eyeball). Cite the computed ratio in the finding.
4. **For keyboard findings**, mentally walk the tab order and key handlers, or run the SPA and Tab through the affected surface. The local dev credentials (`admin`/`admin`) are in auto-memory for spot-checking.
5. **Drop the finding if verification reveals it's not real.** Don't list speculative items.

This mirrors the verification protocol in `audit-cleanup` and `audit-security` — verified findings are what separate this audit from a bare grep.

## Output

Markdown report with:

### 1. Punch list
Grouped by concern (A–K above). Each finding:

```
**[H/M/L] file/path.tsx:NN — short title**
- What: one-line description of the issue
- Why it matters: one-line concrete impact on AT users (screen-reader, keyboard-only, low-vision, motor-impaired)
- WCAG / 508: success criterion (e.g. "WCAG 1.1.1 / 508 §501")
- Fix: one-line concrete remediation (or pointer to a Mantine prop / skill that solves it)
```

Cap at the 15 most impactful findings. Lower-priority items go in a "Also seen" footnote.

**Severity rubric (508-specific)**:
- **High** — blocks an AT user from completing a primary task (login, primary workflow, settings). Includes any contrast failure on a default theme combination.
- **Medium** — degrades the AT experience but a workaround exists (icon button with no label inside a labeled toolbar, missing announcement for a non-critical status).
- **Low** — defense-in-depth or cosmetic (a single decorative image missing `alt=""`, a non-critical animation without a reduced-motion override).

### 2. What I checked and found clean

Short bulleted list per concern (A–K) so the user knows what surface was actually examined. E.g. "F: scanned every page under `src/AutoNate.Spa/src/pages/` (NN files) for heading hierarchy; only the Settings page skips h2 → h4."

### 3. Recommended tooling additions

A separate short section calling out the deltas under concern K — these are usually the highest-ROI follow-ups because they catch *future* regressions without re-running the audit. Suggest the concrete `npm i -D` lines and the minimal ESLint / Playwright config snippet.

### 4. Out of scope

- PR-diff a11y review → no dedicated skill yet; a future `/review-a11y` could fit. For now, this audit is the codebase-wide pass.
- Visual / UX design review (heuristic evaluation, usability testing) — not a 508 concern per se.
- Procurement / VPAT documentation — that's a compliance artifact derived from this audit, not the audit itself.
- Non-HTML deliverables (PDFs the server exports, downloadable Office docs) — separate concern; flag if encountered but don't deep-dive.

## Notes

- 508 / WCAG criteria evolve. WCAG 2.1 (2018) and 2.2 (2023) add criteria around touch targets, focus appearance, accessible authentication, and dragging movements. This audit defaults to **WCAG 2.0 AA** (the floor 508 mandates) but calls out where adopting 2.1 / 2.2 criteria would be cheap given current Mantine support.
- The `mantine-custom-components` and `mantine-combobox` project skills cover the framework-level wiring for accessible custom widgets — when this audit surfaces a custom-widget finding, the matching skill is usually the remediation path.
- This audit is read-only — it produces a report. Acting on findings is a separate task (per finding, or batched via a follow-up PR).
