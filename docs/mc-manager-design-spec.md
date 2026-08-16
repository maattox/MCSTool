# mc manager — UI Design Specification

**Status (2026-08-15):** Visual **reference** for Phase B Blazor Hybrid (WPF + WebView2), not an Avalonia implementation brief. Do **not** scaffold Avalonia from this file. Layout and tokens here are **not locked**; operator notes override. Prefer [`mc-manager-ui-mockup.html`](mc-manager-ui-mockup.html) + [`Blazor-UI-Migration-Plan.md`](Blazor-UI-Migration-Plan.md) (light warm-gray theme from B2). Historical body below still says Avalonia because that was the original target.

This document describes the mockup design for the "mc manager" desktop
app (a Minecraft server management tool). The mockup
was built and iterated on as an HTML/CSS/JS prototype; this spec translates that
design into requirements for the real Manager UI, including notes
on where the two platforms diverge.

**Target window size:** 960 × 640 px (default). Layout should be as fluid as
reasonably possible above/below this, but 960×640 is the size to design and test
against first.

---

## 1. Design principles

These are the "why" behind the specific decisions below — keep them in mind if
you (the implementing agent) need to make a judgment call not covered explicitly.

- **Dark, technical, "trustworthy admin tool" aesthetic.** Borrow visual
  language from tools like Pterodactyl Panel, UniFi Network, and Portainer:
  dark background, monospace for live/technical data, clean sans-serif for UI
  chrome and labels, restrained color use.
- **Color is reserved for state, not decoration.** Green = running / positive /
  success. Red = stop / danger / destructive. Blue accent = interactive /
  progress / informational. Everything else stays neutral gray. Don't introduce
  new colors for things that aren't state.
- **Explicit "Save changes" for batched settings; no save step for one-shot
  actions.** Anything that edits a *set* of settings that get pushed to a VM or
  to cloud object storage (the whitelist, the idle agent config, the usage
  budget) needs its own "Save changes" button, disabled until something
  actually changed, because API calls are rate-limited and shouldn't fire on
  every keystroke. Anything that's a single immediate action (Stop, Restart,
  Deploy/repair infrastructure, Disable guardrails) fires immediately with no
  save step, because there's nothing to batch.
- **Update forms only enable "Update" once something changed.** Prevents
  submitting no-op API calls.
- **Hover-reveal actions on list rows.** Rows the user mostly just reads
  (backups, whitelist entries) keep their per-row action buttons hidden until
  hovered, to reduce visual clutter.
- **Fixed-width label/value alignment.** The status panel's labels and values
  are aligned in fixed-width columns so the layout doesn't jitter as values
  change length (e.g. "2 / 10" vs "10 / 10" players).

---

## 2. Design tokens (colors, fonts, radius)

These are the exact values used in the HTML prototype's standalone export.
Treat them as a starting, internally-consistent palette — not a locked brand
identity — but keep them unless there's a reason to change them, since all the
component-level color decisions below assume this palette.

| Token | Value | Usage |
|---|---|---|
| `bg` | `#0d0f13` | App/window background |
| `surface-1` | `#15181d` | Cards, panels, input backgrounds |
| `surface-2` | `#1b1f26` | Slightly-raised panels (nested inside surface-1 areas), modal cards |
| `border` | `#262b33` | Default hairline borders/dividers |
| `border-strong` | `#3a4048` | Button borders, more visible dividers |
| `border-danger` | `#5c2a2f` | Danger-state button/card borders |
| `text-primary` | `#e7e9ec` | Primary text |
| `text-secondary` | `#aab0ba` | Secondary text (descriptions, mono data) |
| `text-muted` | `#6b7280` | Labels, captions, disabled text |
| `text-success` | `#4ade80` | "Online" status text, positive values |
| `text-danger` | `#f87171` | Danger text (danger zone, stop-state) |
| `text-accent` | `#60a5fa` | Accent text (e.g. admin checkmark) |
| `fill-accent` | `#5b8def` | Primary button fill, progress bar fill, active tab underline |
| `fill-success` | `#34d399` | Success dot fill |
| `fill-danger` | `#f87171` | Danger fill |
| `bg-danger` | `rgba(248,113,113,0.08)` | Danger zone card background tint |
| `font-sans` | Inter | UI chrome, labels, buttons |
| `font-mono` | JetBrains Mono | Status panel, IPs, numeric/tabular data |
| `radius` | `10px` | Standard card corner radius |

Buttons (default/neutral state): `surface-1` background, `border-strong`
border, `text-primary` text, `8px` corner radius.

---

## 3. Layout overview

```
┌─────────────────────────────────────────────────────────────┐
│ [icon] mc manager                [update available][🔔][☰] │  ← header, ~header row
├───────────────────────┬───────────────────────────────────────┤
│ status panel (330px)  │  stat card  │  stat card              │
│  status / play ip /   │  (Daily avg │  (Total usage           │
│  players              │  uptime)    │  this month)             │
│                        ├─────────────┼──────────────────────────┤
│ [Start][Stop][Restart] │  stat card  │  stat card              │
│                        │  (Today's   │  (Rollover               │
│                        │  uptime)    │  bank)                   │
├───────────────────────┴───────────────────────────────────────┤
│ Server management | Whitelist | Advanced/danger zone | Usage  │  ← tab bar
├─────────────────────────────────────────────────────────────┤
│                     [active tab content]                      │
└─────────────────────────────────────────────────────────────┘
```

- **Header row:** app icon + "mc manager" wordmark, left. Right-aligned cluster,
  left-to-right: "update available" pill (only shown when an update exists),
  bell/notifications icon (red dot badge when there are unread notifications),
  hamburger menu icon.
- **Top section:** a two-column row. Left column is a **fixed ~330px** wide
  block containing the status panel card and the three action buttons stacked
  below it. Right column is **flexible width**, filling the remaining space
  with a 2×2 grid of stat cards. At 960px total width this puts the left
  column at roughly 34% and the right column at roughly 66%.
- **Tab bar:** four tabs, underline-style active indicator (2px, `fill-accent`
  color), inactive tabs in `text-muted`.
- **Tab content area:** swaps based on selected tab; only one tab's content is
  visible at a time.

---

## 4. Header

- Left: server icon + "mc manager" text, `text-secondary`/`text-primary`,
  ~15px.
- Right, in this exact order left-to-right: **update-available button → bell
  icon → menu icon.**
  - **Update available button:** small pill button, sparkle icon + "update
    available" text. Only rendered when an update is actually available.
  - **Bell icon:** icon-only button. Small red dot badge overlaid top-right
    corner when there are unread notifications. Click opens the Notifications
    modal (see §8).
  - **Menu icon (hamburger):** click toggles a small anchored dropdown
    directly below the icon (not dimmed/modal — a lightweight popover)
    listing: **About**, **Donate**, **Troubleshooting**. Clicking an item
    closes the dropdown and opens the dimmed modal (§8) with placeholder
    content for that item — exact content TBD later, use "Placeholder
    content — not decided yet." Dropdown also closes on any outside click.

---

## 5. Status panel + action buttons (top-left, ~330px wide)

### Status panel (card)
Three rows, each a fixed-width label column (~70px) followed by a value,
monospace font throughout:

1. **status** — colored dot (green `fill-success` when online, muted gray when
   stopped) + colored text ("online" in `text-success`, "stopped" in
   `text-muted`).
2. **play ip** — the server's connection IP (e.g. `129.146.82.14`) + a small
   "copy" button (copy icon) beside it. On click, briefly swaps to a
   checkmark + "copied" for ~1.2s, then reverts.
3. **players** — current/max, e.g. `2 / 10`.

There is **no uptime row in this panel** — uptime/budget info was moved out to
the stat cards (§6) so it's visible without switching tabs, but isn't
duplicated inside this panel.

### Action buttons
Three large, equal-width buttons directly below the status panel:

- **Start** — play icon + "Start". Outlined style, `text-success` colored
  border/text when enabled. **Disabled (greyed, `text-muted`/`border`,
  ~50% opacity, not clickable) whenever the server status is "online."**
- **Stop** — stop icon + "Stop". Outlined style, `text-danger` colored
  border/text when enabled. **Disabled (same greyed treatment) whenever the
  server status is "stopped."**
- **Restart** — refresh icon + "Restart". Neutral style (`surface-1`
  background, `border-strong` border), **always enabled**, no state
  dependency.

Start and Stop are mutually exclusive by design — only one is ever clickable
at a time, matching the server's actual running/stopped state. They are two
independent buttons, not one button that swaps its label.

---

## 6. Stat cards (top-right, 2×2 grid)

Four cards, equal size, arranged left-to-right/top-to-bottom in this exact
order:

1. **Daily avg uptime** — e.g. "6.2h / 6.5h budget", thin progress bar below
   (fill % = actual/budget).
2. **Total usage this month** — e.g. "43% of monthly cap", thin progress bar
   below (fill % = the percentage itself).
3. **Today's uptime** — e.g. "2.2h / 6.0h allowed", thin progress bar below.
4. **Rollover bank** — e.g. "+12.4h" in `text-success` color, caption below:
   "unused budgeted hours saved". **No progress bar** on this one (it's a
   running total, not a bounded quantity).

Each card: `surface-1` background, `radius` corners, muted label on top,
larger value text below, progress bar (where present) beneath that.

---

## 7. Tabs

### 7.1 Server management (default active tab)

- **Info card row** (4 equal-width cards): **Server name** (e.g.
  "Crafthouse"), **Minecraft version** (e.g. "1.21.1"), **Last backup** (e.g.
  "18 min ago"), **Backup storage** (e.g. "6.2 / 9.5 GB" — this is *storage
  used vs. the max allowed*, not a single backup's size, hence "storage" not
  "size").
- **World backups panel:**
  - Header row: "World backups" title, right-aligned buttons **"Upload
    backup"** (upload icon) and **"Download latest world save"** (download
    icon).
  - List of backup entries below, one per row: filename + creation
    date/time (two lines: filename in `text-primary`, date in smaller
    `text-muted`), file size (`text-muted`, middle/right), and **Download** /
    **Delete** buttons that are only visible when the row is hovered
    (otherwise hidden — opacity 0 → 1 on row hover). Rows separated by a thin
    top border.
  - No "Save changes" button here — upload/download/delete are treated as
    one-shot immediate actions, not batched settings.

### 7.2 Whitelist

- **Allowed IPs panel:**
  - Header row: "Allowed IPs" title, right-aligned buttons **"Add IP"** and
    **"Add IP range"**.
  - Column header row: NAME / IP or RANGE / ADMIN / (blank, for actions).
  - List of entries. Each row:
    - **Name**
    - **IP or CIDR block.** Single IPs are shown *without* a `/32` suffix
      (e.g. `98.42.11.6`, not `98.42.11.6/32`). Ranges show the CIDR notation
      as entered (e.g. `98.42.11.0/24`).
    - **Admin** — a checkmark icon (accent color) if the entry is an admin,
      otherwise blank.
    - **Update** / **Remove** buttons, right-aligned, **only visible on row
      hover.** "Remove" is labeled with the word "Remove" (not just an icon),
      styled in danger color.
  - **"Save changes" button** below the panel — starts disabled/greyed, and
    only becomes enabled once the whitelist data actually changes (add,
    update, or remove). Clicking it commits the batch and disables itself
    again.
- **Add IP modal:** fields — Name (text), IP address (text), Admin
  (checkbox). Buttons: "Add" (primary) / "Cancel".
- **Add IP range modal:** same shape, but the second field is labeled "CIDR
  block" instead of "IP address". Entries added this way display with CIDR
  notation in the list and are internally flagged as ranges (affects whether
  the `/32` suffix stripping applies, and what the Update modal's field label
  says).
- **Update modal:** same fields as Add, pre-filled with the entry's current
  values. **"Update" submit button starts disabled and only becomes enabled
  once at least one field's value differs from its original value.**
  "Cancel" closes without applying anything.
- **Unsaved-changes guard:** if the whitelist tab has unsaved changes and the
  user tries to switch to a different tab, intercept the switch and show a
  confirmation modal with three options: **"Save and continue"**, **"Discard
  changes"**, **"Cancel"** (stays on the current tab). Only after "Save and
  continue" or "Discard changes" does the tab switch actually happen.

### 7.3 Advanced / danger zone

Three separate small panels for server control, **not merged into one panel**
(an earlier iteration tried combining them into a single "Servers" card; it
was reverted — keep them separate):

1. **Door VM** — "Small always-on VM that listens for connections and wakes
   the main VM." Buttons: Stop, Restart.
2. **Main VM** — "The VM that actually runs the Minecraft server." Buttons:
   Stop, Restart.
3. **Minecraft process** — "Restart just the game, keeping both VMs running."
   Single button: **"Restart Minecraft Server"** (exact label — not just
   "Restart Minecraft").

**Idle agent panel** (separate card, larger):
- Title + description ("Automatically stops the server when nobody's online,
  and warns players before the daily budget runs out.")
- **Enabled** toggle switch (on/off).
- Two numeric fields: **"Idle timeout (minutes)"** and **"Budget warning lead
  time (minutes)"** (how long before the daily budget limit is reached that
  players get warned).
- **"Save changes" button** — disabled until the toggle or either field
  changes. This panel is a batched-settings panel (distinct from the one-shot
  VM/process action panels above), so it follows the Save-changes pattern.

**Infrastructure setup panel** (separate card):
- Title + description.
- Single button: **"Deploy / repair infrastructure"** — one-shot immediate
  action, no Save step.

**Danger zone row** (visually distinct — `bg-danger` tinted background):
- Title in `text-danger`: **"Disable idle and budget guardrails"**
- Description in `text-danger`: "Testing only. Doesn't survive a restart — the
  VM always re-enables guardrails on boot."
- Button: **"Disable"**, danger-outlined.

### 7.4 Usage

- Month header line (small, muted): e.g. "August 2026 · day 14 of 31".
- **Table** — rows: **Hard cap**, **Soft cap**, **Used month-to-date**.
  Columns: **Server uptime (hrs)**, **OCPU (hrs)**, **Memory (hrs)**. Numbers
  right-aligned, tabular/fixed-width figures.
  - Note: the user wasn't fully satisfied these row/column labels are the
    clearest possible framing, but didn't have a better alternative at time
    of writing — flagged as an open item, not a firm requirement.
- **Usage budget panel** (card): description ("More budget fields will likely
  be added here later" — expect this section to grow). Two numeric fields:
  **"Monthly hard cap (OCPU hrs)"**, **"Monthly soft cap (OCPU hrs)"**.
  **"Save changes" button**, disabled until a field changes.
- **Important:** the four stat cards (Daily avg uptime / Total usage this
  month / Today's uptime / Rollover bank) that conceptually belong to "usage"
  live in the always-visible top section (§6), **not** in this tab — don't
  duplicate them here.

---

## 8. Modals / popups (shared pattern)

Every popup in this design (except the lightweight menu dropdown) follows the
same pattern: a full-window semi-transparent dark overlay (~55% black) that
dims everything behind it, with a centered card (rounded corners, ~340px
wide, capped height with internal scrolling if content overflows) on top.
Clicking outside the card (on the dimmed overlay) or a "Cancel"/"Close" button
dismisses it.

Used for:
- The unsaved-changes prompt (§7.2)
- Add IP / Add IP range / Update entry forms (§7.2)
- About / Donate / Troubleshooting placeholder screens (§4)
- The Notifications list

**Notifications modal:** scrollable list of past notifications, **most recent
first**, each entry showing a date/time stamp clearly (e.g. top-right corner
of the entry) and the notification text below it. Exact notification types
and triggering logic are still undecided — treat the current set as
placeholder examples, not a spec.

---

## 9. Interaction states summary

| Element | Default | On hover | Enabled/disabled logic |
|---|---|---|---|
| Backup row actions (Download/Delete) | hidden | visible | always both enabled |
| Whitelist row actions (Update/Remove) | hidden | visible | always both enabled |
| Start button | — | — | disabled when status = online |
| Stop button | — | — | disabled when status = stopped |
| Restart button | — | — | always enabled |
| "Save changes" buttons | disabled | — | enabled once the relevant tab's data has an unsaved change |
| Update-entry modal submit | disabled | — | enabled once a field differs from its original value |

---

## 10. Notes for the Avalonia implementation

The mockup was prototyped in HTML/CSS/JS for fast iteration. Several things
translate directly; a few need a different approach in Avalonia. Notes below
are grouped by what needs to change vs. what's actually *easier* to do
properly in Avalonia than it was in the web mockup.

### 10.1 Colors → resources
Define the token table in §2 as `SolidColorBrush` resources (e.g. in a
`Theme.axaml` `ResourceDictionary`), referenced via `{DynamicResource ...}` so
the palette stays centralized and swappable:
```xml
<SolidColorBrush x:Key="Surface1">#15181D</SolidColorBrush>
<SolidColorBrush x:Key="TextSuccess">#4ADE80</SolidColorBrush>
<!-- etc. -->
```
Consider whether to build on top of Avalonia's `FluentTheme` (dark variant) or
go fully custom — either is workable, but the specific brushes above should
win regardless of base theme.

### 10.2 Fonts must be bundled, not CDN-loaded
The mockup pulls Inter and JetBrains Mono from Google Fonts via a `<link>`
tag — that has no equivalent in a desktop app. **Bundle the actual font
files** (both are free/open — Inter is SIL OFL, JetBrains Mono is Apache 2.0)
under something like `Assets/Fonts/` in the project and reference them via:
```xml
FontFamily="avares://YourAppName/Assets/Fonts#Inter"
```

### 10.3 Icons need a different source
The mockup uses the Tabler Icons **webfont** via a CDN stylesheet and
`<i class="ti ti-xxx">` tags. There's no direct Avalonia equivalent. Options:
- Use a NuGet icon package that includes a Tabler set (e.g.
  `Projektanker.Icons.Avalonia` supports multiple icon packs including
  Tabler) — closest to a drop-in replacement.
- Or export the specific glyphs used as SVGs / `PathIcon` geometry and bundle
  them as resources.

Icons actually used in this design (make sure whichever source you pick
covers all of these): `server-2`, `sparkles`, `bell`, `menu-2`, `copy`,
`check`, `player-play`, `player-stop`, `refresh`, `plus`, `upload`,
`download`. (`x`, `info-circle`, `heart`, `lifebuoy` appeared in earlier
mockup iterations but aren't required by the current design — the placeholder
About/Donate/Troubleshooting screens don't have finalized icons yet.)

### 10.4 Hover-reveal row actions — translates cleanly
CSS `:hover` → Avalonia's `:pointerover` pseudoclass selector in a `Style`,
toggling the action panel's `Opacity` (with a `Transitions` animation for the
fade). This is a straightforward, well-supported pattern in Avalonia — no
special caveats.

### 10.5 Fixed-width label/value alignment — easier in Avalonia
The status panel's label/value alignment was done with CSS Grid and a fixed
first-column width. In Avalonia, a `Grid` with explicit
`ColumnDefinitions="70,*"` (or similar) does this more robustly and directly
— this is arguably a cleaner primitive here than the CSS equivalent, not a
compromise.

### 10.6 Progress bar width matching text — do it properly here
In the HTML mockup, the mini progress bars use a **fixed pixel width** as a
stand-in for "match the width of the text above it," because reliably sizing
one element to another's *rendered text width* isn't clean in static CSS. In
Avalonia, don't carry over that approximation — bind the `ProgressBar`'s
`Width` to the sibling `TextBlock`'s actual rendered width (or wrap both in a
`Width="Auto"` container), which is straightforward with Avalonia's layout
system and will look more correct than the web version did.

### 10.7 Modal/dialog pattern
The web version hand-built its modal system (fixed-position overlay `div` +
centered card, JS-driven show/hide). Avalonia doesn't have this out of the
box. Recommended approach: an `OverlayLayer`/`Panel` within the main window
containing a semi-transparent `Border` behind a centered content `Border`,
toggled via an `IsVisible` binding — this reproduces the "dim the same window
in place" look the mockup uses (a separate `Window` via `ShowDialog` would pop
a new OS-level window instead, which looks different). Community packages
like `DialogHost.Avalonia` implement this in-place-overlay pattern already and
may save time versus building it from scratch.

### 10.8 Toggle switch
Don't hand-roll the Idle Agent "Enabled" toggle (the HTML mockup used a
styled checkbox because that's what's available in plain HTML) — Avalonia has
a built-in `ToggleSwitch` control; use that directly.

### 10.9 "Dirty"/Save-changes tracking
The web mockup tracks per-tab "dirty" state and disables Save buttons
imperatively in JavaScript. In Avalonia with any MVVM setup, this should map
to a bound `IsDirty` (or `CanSave`) boolean per view model, with each Save
button's `IsEnabled` bound to it — cleaner than the JS version, no
translation caveats, just implement it as normal MVVM state.

### 10.10 Tabular/fixed-width numbers
JetBrains Mono is monospace, so every character is already a fixed width —
there's no need for anything like CSS's `font-variant-numeric: tabular-nums`
in Avalonia. Row-width stability comes from the fixed-width *containers*
(§10.5), not from special numeral handling — no extra work needed here beyond
the Grid columns already covered.

### 10.11 Window sizing
Set the main `Window`'s default size to `Width="960" Height="640"`. Build the
internal layout with proportion-friendly containers (`Grid` with star-sized
columns/rows, `WrapPanel` where wrapping is acceptable) rather than hardcoded
pixel values throughout, matching the "mostly fluid, tuned for 960×640"
approach used in the mockup. The one concrete ratio to carry over: the
top-section left column (status panel + buttons) is fixed at **330px**, with
the stat-card grid taking the remaining space — roughly a 330:630 (~34%/66%)
split at the 960px target width.

---

## 11. Open items / things the user hasn't decided yet

Flagging these so the next agent doesn't treat them as settled:
- Exact content of the About / Donate / Troubleshooting screens.
- Real notification types, triggering logic, and formatting beyond the
  placeholder examples shown.
- Whether the Usage tab's table row/column labels ("Hard cap" / "Soft cap" /
  "Used month-to-date") are the final wording — user flagged uncertainty here.
- Additional fields likely to be added to the Usage budget panel later.
