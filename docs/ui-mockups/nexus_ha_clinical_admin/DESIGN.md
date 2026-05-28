---
name: Nexus HA Clinical Admin
colors:
  surface: '#f9f9ff'
  surface-dim: '#d3daea'
  surface-bright: '#f9f9ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f0f3ff'
  surface-container: '#e7eefe'
  surface-container-high: '#e2e8f8'
  surface-container-highest: '#dce2f3'
  on-surface: '#151c27'
  on-surface-variant: '#464555'
  inverse-surface: '#2a313d'
  inverse-on-surface: '#ebf1ff'
  outline: '#777587'
  outline-variant: '#c7c4d8'
  surface-tint: '#4d44e3'
  primary: '#3525cd'
  on-primary: '#ffffff'
  primary-container: '#4f46e5'
  on-primary-container: '#dad7ff'
  inverse-primary: '#c3c0ff'
  secondary: '#006591'
  on-secondary: '#ffffff'
  secondary-container: '#39b8fd'
  on-secondary-container: '#004666'
  tertiary: '#46494a'
  on-tertiary: '#ffffff'
  tertiary-container: '#5e6061'
  on-tertiary-container: '#dadbdc'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#e2dfff'
  primary-fixed-dim: '#c3c0ff'
  on-primary-fixed: '#0f0069'
  on-primary-fixed-variant: '#3323cc'
  secondary-fixed: '#c9e6ff'
  secondary-fixed-dim: '#89ceff'
  on-secondary-fixed: '#001e2f'
  on-secondary-fixed-variant: '#004c6e'
  tertiary-fixed: '#e1e3e4'
  tertiary-fixed-dim: '#c5c7c8'
  on-tertiary-fixed: '#191c1d'
  on-tertiary-fixed-variant: '#454748'
  background: '#f9f9ff'
  on-background: '#151c27'
  surface-variant: '#dce2f3'
  status-active: '#10B981'
  status-pending: '#F59E0B'
  status-error: '#EF4444'
  status-verified: '#6366F1'
  status-archived: '#374151'
  surface-card: '#FFFFFF'
  surface-bg: '#F3F4F6'
typography:
  display-lg:
    fontFamily: Roboto
    fontSize: 30px
    fontWeight: '700'
    lineHeight: 38px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Roboto
    fontSize: 20px
    fontWeight: '500'
    lineHeight: 28px
  body-base:
    fontFamily: Roboto
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 18px
  body-sm:
    fontFamily: Roboto
    fontSize: 11px
    fontWeight: '400'
    lineHeight: 16px
  label-bold:
    fontFamily: Roboto
    fontSize: 12px
    fontWeight: '700'
    lineHeight: 16px
  code-mono:
    fontFamily: JetBrains Mono
    fontSize: 11px
    fontWeight: '400'
    lineHeight: 16px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  margin-page: 24px
  gutter-grid: 16px
  padding-card: 20px
  sidebar-width: 240px
  stack-sm: 4px
  stack-md: 12px
  stack-lg: 24px
---

## Brand & Style

The design system is engineered for high-compliance healthcare administration, prioritizing precision, security, and institutional trust. The visual narrative balances clinical rigor with modern accessibility, ensuring that complex data remains legible and actionable under high-cognitive-load environments.

The aesthetic follows a **Corporate / Modern** style with subtle **Minimalist** influences. It utilizes a structured "Information First" hierarchy, where white space is used strategically to separate distinct administrative domains (Patients, Doctors, Branches, and Audits). The interface should feel "clean but robust," evoking the reliability of medical grade software through sharp execution and predictable layouts.

## Colors

The palette is anchored by a vibrant Indigo primary color, providing a professional "Winning" dashboard aesthetic while maintaining clear contrast for healthcare workflows.

- **Primary & Secondary:** Used for high-priority actions, active navigation states, and brand-critical touchpoints.
- **Surface & Backgrounds:** A tiered system of "Soft Whites" and "Light Grays" creates depth without relying on heavy borders.
- **Functional Semantics:** Colors are strictly mapped to the system's state machine. Green (`status-active`) and Indigo (`status-verified`) represent positive progression, while Amber (`status-pending`) and Red (`status-error`) highlight areas requiring administrative attention. 
- **Neutral Scale:** Grays are tuned to ensure that metadata—like UUIDs and Trace IDs—remains readable but secondary to primary human labels.

## Typography

This design system uses **Roboto** as its sole typeface family to ensure maximum cross-platform compatibility and a familiar, professional tone.

The typography scale is intentionally dense, reflecting the high-information requirements of medical administration. 
- **Base size:** 12px for body content ensures high data density in tables and lists.
- **Monospaced Content:** Technical identifiers (Public Codes, UUIDs, and Trace IDs) must be rendered in a monospaced variant to distinguish system-generated strings from human-entered data.
- **Hierarchical Contrast:** Bold weights and slight negative letter spacing are reserved for titles to provide clear section breaks within information-heavy layouts.

## Layout & Spacing

The layout utilizes a **Fixed Sidebar + Fluid Content** model. The sidebar remains fixed at 240px to provide constant access to core administrative modules (Patients, Doctors, Branches, Audits).

- **Grid:** A 12-column fluid grid system governs the main content area. Content is housed within "Clean Cards" that span variable column widths (e.g., 4 columns for small stats, 8-12 columns for data tables).
- **Density:** Spacing units follow a 4px base scale. The default pagination density is set to 20 items per view to prevent visual overwhelm while allowing users to scale up to 100 items for bulk auditing tasks.
- **Mobile Adaptivity:** On mobile, the sidebar collapses into a bottom navigation bar or a hamburger menu, and cards stack vertically with reduced horizontal margins (16px).

## Elevation & Depth

Visual hierarchy is established through **Tonal Layers** rather than aggressive shadows, mimicking the "Winning" dashboard reference.

- **Level 0 (Background):** Soft gray (`surface-bg`) provides a neutral canvas.
- **Level 1 (Containers):** Cards use white backgrounds with a delicate 1px border or a very low-opacity (2-4%) ambient shadow to appear slightly lifted.
- **Level 2 (Overlays):** Modals and Toasts (Problem Details) use more pronounced, diffused shadows to indicate their temporary nature and focus.
- **Interaction:** Hover states on list items and interactive cards use a subtle "tint" shift (e.g., primary-light background) rather than physical elevation changes to maintain a flat, clinical feel.

## Shapes

The design system employs a **Rounded** shape language to soften the clinical nature of the data. 

- **Primary Components:** Cards, buttons, and input fields use a 0.5rem (8px) radius.
- **Status Badges:** Use a "Full Pill" (rounded-xl) shape to distinguish status indicators from clickable buttons.
- **Visual Rhythm:** Consistent rounding across all interface elements ensures a cohesive, modern look that aligns with the "Winning" dashboard visual reference.

## Components

### Buttons
Primary buttons use the indigo hex with white text. Secondary buttons use a "Ghost" style (indigo border/text on transparent). All buttons feature an 8px radius and 12px Roboto Medium text.

### Status Badges
High-priority components in this system. They must use the semantic color scale (e.g., soft green background with dark green text for "Active"). Text should be 11px Bold Uppercase.

### Data Tables
Tables are the backbone of the system. Rows should have a 48px minimum height, with subtle 1px dividers. The first column (usually Public Code or Name) should be "Strong" weight.

### Input Fields
Outlined style with a 1px gray-300 border. Focus states transition the border to the primary indigo. Helper text and error messages must align with the `ProblemDetails` schema from the API.

### Cards
White surfaces with 20px internal padding. Card headers should contain the title in 14px Medium and optional "Action" icons on the right.

### Audit Ledger
A specialized list component that uses a monospaced font for "Before/After" JSON state changes and timestamps, ensuring technical accuracy is visually distinct from the rest of the UI.