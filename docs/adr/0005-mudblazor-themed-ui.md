---
status: accepted
---

# MudBlazor themed to the original look, instead of porting Tailwind

The React UI was hand-styled with Tailwind utility classes. Instead of porting those classes, the Blazor UI is built
from MudBlazor 9 components (data grid, dialogs, snackbars, chips, progress bars) with a custom theme carrying the
original palette (#09090B, #18181B, #3B82F6, #EF4444, #71717A), Inter and JetBrains Mono, uppercase monospace labels,
and the original layout. This trades pixel fidelity for far less hand-written CSS and standard, accessible widgets.

## Considered Options

- Port the Tailwind classes with the Tailwind CSS v4 standalone CLI, or v3 through npm.
- Microsoft Fluent UI Blazor or Radzen Blazor instead of MudBlazor.

## Consequences

- Replacing the component library later means rewriting the views.
- Where the original's text colours fall below WCAG 2.2 AA contrast, the theme uses lighter shades of them: #A1A1AA for
  small grey text, and #60A5FA and #F87171 for blue and red text on tinted backgrounds (U5, Q-U5-N1).
