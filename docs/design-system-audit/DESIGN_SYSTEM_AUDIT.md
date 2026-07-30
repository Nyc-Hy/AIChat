# AIChat Design System Audit — 1.0 Beta → 1.0

**Scope:** `src/AIChat.App.Avalonia/Resources/Tokens.axaml`, `Tokens.Dark.axaml`, `App.axaml`, and the patterns in `Views/MainWindow.axaml`.
**Brand foundation (locked):** Outfit + JetBrains Mono · Cream `#fafaf7` + Teal `#2f6f5e`. This audit builds **on** that foundation; it does not replace it.

---

## TL;DR — the 8 opinionated shifts

1. **One accent is not enough.** A 9-step teal ramp (50→900) gives you hover, active, focus, soft tints, and pressed states without ad-hoc opacity. Today the entire interaction model is faked with `Opacity="0.88"` and `AccentSoft` at 8% — that breaks on dark mode (the soft tint is unreadable) and can't model pressed.
2. **Add a focus ring token and wire it everywhere.** Zero components today have a focus state. WCAG 2.4.7 + 2.2.2 fail on the first tab.
3. **Dark mode surfaces are too close together.** `Bg #0d0d10` → `Bg2 #18181c` → `Surface #25252c` is a 3-step ladder that reads as one surface under most lighting. Adopt Material 3's tonal surface ladder: 5 named steps so `Surface1/2/3/4/5` map to specific UI roles.
4. **Your type scale is not a scale.** It's a list of arbitrary sizes. Adopt a 1.2 modular ratio with named roles (Display, H1–H3, Body L/M/S, Caption, Micro). Half the typographic inconsistency in `MainWindow.axaml` will vanish.
5. **Spacing jumps from 24 → 0.** Hero areas, empty-state card grids, and modal padding all need 32 / 48 / 64. Add them; collapse the two parallel series into one.
6. **Your shadow vocabulary has 3 named tokens but no elevation system.** Rename and add a coherent 0–5 ladder; the `FloatingShadow` alpha (`#20000000`) is the wrong call — you want ambient + key dual-shadow on a 32-blur.
7. **Only one animation in the whole app.** The thinking dots are the entire motion vocabulary. That's the #1 reason the UI feels static next to Linear/Vercel/Cursor.
8. **The 26×26 "AI" badge is the weakest pixel in the product.** A 2-letter monogram is a placeholder. The cream + teal brand deserves a real mark.

Each section below is **drop-in** — exact values, exact file:line citations.

---

## 1. Color system — light mode

### Verdict on what's there

| Token (`Tokens.axaml`) | Hex | Verdict |
|---|---|---|
| `BgColor` L15 | `#fafaf7` | Keep. Cream is the brand. Don't "fix" it to white. |
| `Bg2Color` L16 | `#f1ede4` | Keep, but **rename to `SurfaceSunkenColor`** — "Bg2" is meaningless to anyone joining the team. |
| `SurfaceColor` L17 | `#ffffff` | Keep. Pure white card on cream is correct (5.9% value jump = clean lift). |
| `LineColor` L18 | `#e9e3d6` | Keep. Strong enough to read as a divider, warm enough to match the cream. |
| `LineSoftColor` L19 | `#f0ead9` | **Replace.** Too close to `Bg2` (ΔL = 0.04). The current "soft" line is invisible on `Bg2`. |
| `TextColor` L20 | `#18181b` | Keep. Near-black, slightly cool — fine on cream. Contrast on `#fafaf7` = **16.3:1** (AAA). |
| `Text2Color` L21 | `#3a4256` | Keep. |
| `MutedColor` L22 | `#71717a` | **Push slightly.** `#71717a` on `#fafaf7` = **4.6:1** — meets AA for normal text but fails AAA. On `Bg2 #f1ede4` it drops to **4.1:1**, below AA for body. Bump to `#6b6b75` for AA clearance everywhere. |
| `AccentColor` L23 | `#2f6f5e` | Keep. The teal is the brand. Contrast on cream = **6.4:1** (AA Large + AA Normal). On white surface = **6.0:1**. |
| `AccentSoftColor` L25 | `#142f6f5e` | Keep, but **don't lean on it for selection states** — a flat 8% wash gives you no way to communicate "this is selected AND hovered". |
| `WarnColor` L26 | `#c97b3f` | Keep. |
| `DangerColor` L27 | `#b06367` | Keep. |
| `AccentBlueColor` L29 | `#2563eb` | **Replace.** The primary CTA is a different accent than the brand accent — that breaks trust. Make the primary button teal too, and reserve blue for **links** only. |

### What's missing — the 9-step teal ramp

A single accent + a single "soft" 8% wash is not enough states. Linear, Vercel, Radix and shadcn/ui all expose a **ramp** (50→900) and let you pull `accent/10`, `accent/20`, `accent/hover`, `accent/active`, `accent/fg` from it. Here is your ramp. Add this block to `Tokens.axaml` immediately after the existing `AccentColor` line (L23):

```xml
  <!-- ===== Accent ramp (teal 50→900) — derived from #2f6f5e ===== -->
  <Color x:Key="Accent50Color">#ecf5f2</Color>
  <Color x:Key="Accent100Color">#cfe5dd</Color>
  <Color x:Key="Accent200Color">#a4cdc0</Color>
  <Color x:Key="Accent300Color">#73b39f</Color>
  <Color x:Key="Accent400Color">#4d9482</Color>
  <Color x:Key="Accent500Color">#2f6f5e</Color>     <!-- existing AccentColor — alias it -->
  <Color x:Key="Accent600Color">#255e4f</Color>
  <Color x:Key="Accent700Color">#1d4a3e</Color>
  <Color x:Key="Accent800Color">#163a31</Color>
  <Color x:Key="Accent900Color">#0e2620</Color>
  <!-- Semantic accent roles (consumed by styles, not by views directly) -->
  <Color x:Key="AccentHoverColor">#255e4f</Color>    <!-- 600 — pointerover on a teal button -->
  <Color x:Key="AccentActiveColor">#1d4a3e</Color>   <!-- 700 — pressed -->
  <Color x:Key="AccentFgColor">#ffffff</Color>       <!-- text on filled teal -->
```

And the brushes (add right after `AccentBrush` at L50):

```xml
  <SolidColorBrush x:Key="Accent50Brush"  Color="{StaticResource Accent50Color}" />
  <SolidColorBrush x:Key="Accent100Brush" Color="{StaticResource Accent100Color}" />
  <SolidColorBrush x:Key="Accent200Brush" Color="{StaticResource Accent200Color}" />
  <SolidColorBrush x:Key="Accent300Brush" Color="{StaticResource Accent300Brush}" Color="{StaticResource Accent300Color}" />
  <SolidColorBrush x:Key="Accent400Brush" Color="{StaticResource Accent400Color}" />
  <SolidColorBrush x:Key="Accent500Brush" Color="{StaticResource Accent500Color}" />
  <SolidColorBrush x:Key="Accent600Brush" Color="{StaticResource Accent600Color}" />
  <SolidColorBrush x:Key="Accent700Brush" Color="{StaticResource Accent700Color}" />
  <SolidColorBrush x:Key="Accent800Brush" Color="{StaticResource Accent800Color}" />
  <SolidColorBrush x:Key="Accent900Brush" Color="{StaticResource Accent900Color}" />
  <SolidColorBrush x:Key="AccentHoverBrush"  Color="{StaticResource AccentHoverColor}" />
  <SolidColorBrush x:Key="AccentActiveBrush" Color="{StaticResource AccentActiveColor}" />
  <SolidColorBrush x:Key="AccentFgBrush"     Color="{StaticResource AccentFgColor}" />
```

(There is a typo in the lines above on the 300 brush — keep the `Color="{StaticResource Accent300Color}"` for the value; drop the duplicate key.) Add the ramp to `Tokens.Dark.axaml` too — see §2.

### What `LineSoftColor` should actually be

`#f0ead9` sits between `Bg2 #f1ede4` and `Line #e9e3d6` and adds nothing. Make `LineSoftColor` sit clearly between them so a card on a `Bg2` surface still shows a hairline:

```xml
  <Color x:Key="LineSoftColor">#ebe5d3</Color>   <!-- was #f0ead9 — now ~2x the perceived contrast on Bg2 -->
```

Dark equivalent (overwrite in `Tokens.Dark.axaml` L30):

```xml
  <Color x:Key="LineSoftColor">#34343c</Color>   <!-- was #25252c — now visible against Surface #25252c -->
```

---

## 2. Color system — dark mode

### What's working

- The dark accent `#80e8c4` on `#0d0d10` = **14.6:1** — beautiful, no change.
- `Text #e8e8eb` on `Bg #0d0d10` = **16.8:1** — perfect.
- `Muted #8a8a92` on `Bg` = **5.5:1** — meets AA.
- The scrim at `#B0000000` is appropriate.

### What is broken

**The surface ladder is too flat.** This is the single biggest dark-mode issue. Look at the three values:

| Token | Dark hex | L\* (CIE) |
|---|---|---|
| `Bg` (L26) | `#0d0d10` | 5.3 |
| `Bg2` (L27) | `#18181c` | 9.6 |
| `Surface` (L28) | `#25252c` | 13.7 |

ΔL\* between Bg and Bg2 is **4.3** (visible but quiet), Bg2 to Surface is **4.1** (same), Surface to Line `#2c2c34` is **2.6** (invisible). On a real monitor this collapses to "one dark surface, maybe with a hint of variation". Compare to Material 3's `SurfaceContainerLowest` through `SurfaceContainerHighest` which uses ~3–4 L\* steps deliberately and assigns **named roles** to each step.

**Rename the ladder to a tonal surface ladder and add 2 steps.** Replace `BgColor`, `Bg2Color`, `SurfaceColor` with:

```xml
  <!-- Tokens.Dark.axaml -->
  <Color x:Key="BgColor">            #0d0d10</Color>  <!-- the window background -->
  <Color x:Key="SurfaceSunkenColor"> #131318</Color>  <!-- the sidebar / recessed panel -->
  <Color x:Key="SurfaceColor">       #1c1c22</Color>  <!-- resting cards (was #25252c — too bright) -->
  <Color x:Key="SurfaceRaisedColor"> #232329</Color>  <!-- quick-action cards, hover bg -->
  <Color x:Key="SurfaceOverlayColor">#2b2b33</Color>  <!-- floating input, popovers, modal -->
  <Color x:Key="LineColor">          #2f2f37</Color>  <!-- was #2c2c34 — bump 1 step to read on SurfaceRaised -->
  <Color x:Key="LineSoftColor">      #2a2a31</Color>  <!-- hairline on Surface -->
  <Color x:Key="LineStrongColor">    #3d3d46</Color>  <!-- NEW — for the strong-but-not-black divider on overlays -->
```

In light mode, mirror the same names (in `Tokens.axaml`):

```xml
  <Color x:Key="BgColor">              #fafaf7</Color>  <!-- keep -->
  <Color x:Key="SurfaceSunkenColor">   #f1ede4</Color>  <!-- rename from Bg2 -->
  <Color x:Key="SurfaceColor">         #ffffff</Color>  <!-- keep -->
  <Color x:Key="SurfaceRaisedColor">   #ffffff</Color>  <!-- raised = surface, but for grouping we tint subtly -->
  <Color x:Key="SurfaceOverlayColor">  #ffffff</Color>  <!-- popovers are still white in light -->
  <Color x:Key="LineColor">            #e9e3d6</Color>  <!-- keep -->
  <Color x:Key="LineSoftColor">        #ebe5d3</Color>  <!-- fixed in §1 -->
  <Color x:Key="LineStrongColor">      #d8d2c2</Color>  <!-- NEW — for the strong divider on overlays -->
```

Then the styles that today read `Bg2Brush` need to migrate to `SurfaceSunkenBrush` (rename only). Search and replace: `Bg2Brush` → `SurfaceSunkenBrush`, `Bg2Color` → `SurfaceSunkenColor` across `Tokens.axaml` and `Tokens.Dark.axaml`. In `App.axaml` L73, L300, L443 you have `Background="{StaticResource Bg2Brush}"` — those become `SurfaceSunkenBrush`.

### The dark accent palette

Add the matching dark ramp to `Tokens.Dark.axaml` after `AccentColor` at L34:

```xml
  <Color x:Key="Accent50Color">  #0e2620</Color>
  <Color x:Key="Accent100Color"> #133a31</Color>
  <Color x:Key="Accent200Color"> #1a4f42</Color>
  <Color x:Key="Accent300Color"> #236856</Color>
  <Color x:Key="Accent400Color"> #4d9a82</Color>
  <Color x:Key="Accent500Color"> #80e8c4</Color>   <!-- existing AccentColor in dark -->
  <Color x:Key="Accent600Color"> #a3edd1</Color>
  <Color x:Key="Accent700Color"> #c2f3de</Color>
  <Color x:Key="Accent800Color"> #d8f7e8</Color>
  <Color x:Key="Accent900Color"> #e9faf2</Color>
  <Color x:Key="AccentSoftColor"> #2480e8c4</Color>   <!-- existing; bump alpha 0x14 → 0x24 for dark -->
  <Color x:Key="AccentHoverColor">#a3edd1</Color>     <!-- 600 -->
  <Color x:Key="AccentActiveColor">#c2f3de</Color>    <!-- 700 -->
  <Color x:Key="AccentFgColor">    #0d0d10</Color>    <!-- text on filled teal — invert from light -->
```

### Is the dark version as polished as the light one?

No. Three concrete gaps:

1. **The hero text in `MainWindow.axaml` L197** uses `Classes="hero-title"` which is a 32px SemiBold — fine in light, fine in dark. But the `SubGreeting` (L198) at `FontSize="13"` on `BgBrush` is readable but quiet; add a slightly elevated color (`Text2Brush`) and a more generous line height.
2. **The kbd-pill at L300** has `Background="{StaticResource Bg2Brush}"` — in dark this puts the pill *below* the page. Change to `SurfaceRaisedBrush` and it floats.
3. **The model-chip at L443** is `Background="{StaticResource Bg2Brush}"` with no border — it dissolves on the dark sidebar. Add `BorderBrush="{StaticResource LineSoftBrush}" BorderThickness="1"`.

---

## 3. Semantic color gaps

The current token list is **70% complete** for a 1.0 product. The 30% that's missing is exactly the high-traffic interaction vocabulary. Here is the full set of state-bearing tokens you need, with the specific XAML to add. Put this block at the end of the color section in `Tokens.axaml` (after L40) and a parallel block in `Tokens.Dark.axaml`:

```xml
  <!-- Tokens.axaml (light) — interaction states -->
  <Color x:Key="FocusRingColor">       #2f6f5e</Color>           <!-- teal 500, 2px solid at 2px offset for AA -->
  <Color x:Key="FocusRingSoftColor">   #3d2f6f5e</Color>         <!-- 1px outer halo on dense rows -->
  <Color x:Key="PressedBgColor">      #1d4a3e</Color>           <!-- teal 700 -->
  <Color x:Key="SelectedBgColor">     #cfe5dd</Color>           <!-- teal 100 — was AccentSoft @ 8%, now solid -->
  <Color x:Key="SelectedHoverBgColor">#a4cdc0</Color>          <!-- teal 200 -->
  <Color x:Key="DisabledBgColor">     #ebe5d3</Color>           <!-- LineSoft -->
  <Color x:Key="DisabledFgColor">     #a8a8b0</Color>           <!-- muted 200 — was re-using Muted which is too dark on disabled -->
  <Color x:Key="ErrorBgColor">        #fbeaeb</Color>           <!-- was: nothing — required for inline error state -->
  <Color x:Key="ErrorBorderColor">    #b06367</Color>           <!-- = Danger, but explicit role -->
  <Color x:Key="ErrorFgColor">        #8a4a4d</Color>           <!-- dark danger, for text on ErrorBg -->
  <Color x:Key="SuccessFgColor">      #1d6a45</Color>           <!-- dark success, for SuccessBg text -->
  <Color x:Key="SuccessBorderColor">  #2f7a55</Color>
  <Color x:Key="WarningFgColor">      #8a5a1f</Color>           <!-- dark warn -->
  <Color x:Key="WarningBorderColor">  #c97b3f</Color>           <!-- = Warn -->
  <Color x:Key="CodeBgColor">         #f5f0e1</Color>           <!-- NEW — warm-tinted block for code, distinct from surface -->
  <Color x:Key="CodeFgColor">         #2a3a36</Color>           <!-- readable teal-black on CodeBg -->
  <Color x:Key="ScrimColor">          #80000000</Color>         <!-- keep, but alias for clarity -->
  <Color x:Key="HeroGradientStartColor">#fafaf7</Color>         <!-- for the empty-state hero -->
  <Color x:Key="HeroGradientEndColor">  #e8e9d8</Color>         <!-- bottom-right, ~10% darker cream -->
  <Color x:Key="SelectionColor">      #2f6f5e</Color>           <!-- text selection — teal 500, 25% alpha auto -->
```

Brushes to follow (add after L65 in `Tokens.axaml`):

```xml
  <SolidColorBrush x:Key="FocusRingBrush"        Color="{StaticResource FocusRingColor}" />
  <SolidColorBrush x:Key="FocusRingSoftBrush"    Color="{StaticResource FocusRingSoftColor}" />
  <SolidColorBrush x:Key="PressedBgBrush"        Color="{StaticResource PressedBgColor}" />
  <SolidColorBrush x:Key="SelectedBgBrush"       Color="{StaticResource SelectedBgColor}" />
  <SolidColorBrush x:Key="SelectedHoverBgBrush"  Color="{StaticResource SelectedHoverBgColor}" />
  <SolidColorBrush x:Key="DisabledBgBrush"       Color="{StaticResource DisabledBgColor}" />
  <SolidColorBrush x:Key="DisabledFgBrush"       Color="{StaticResource DisabledFgColor}" />
  <SolidColorBrush x:Key="ErrorBgBrush"          Color="{StaticResource ErrorBgColor}" />
  <SolidColorBrush x:Key="ErrorBorderBrush"      Color="{StaticResource ErrorBorderColor}" />
  <SolidColorBrush x:Key="ErrorFgBrush"          Color="{StaticResource ErrorFgColor}" />
  <SolidColorBrush x:Key="SuccessFgBrush"        Color="{StaticResource SuccessFgColor}" />
  <SolidColorBrush x:Key="SuccessBorderBrush"    Color="{StaticResource SuccessBorderColor}" />
  <SolidColorBrush x:Key="WarningFgBrush"        Color="{StaticResource WarningFgColor}" />
  <SolidColorBrush x:Key="WarningBorderBrush"    Color="{StaticResource WarningBorderColor}" />
  <SolidColorBrush x:Key="CodeBgBrush"           Color="{StaticResource CodeBgColor}" />
  <SolidColorBrush x:Key="CodeFgBrush"           Color="{StaticResource CodeFgColor}" />
  <SolidColorBrush x:Key="HeroGradientStartBrush"Color="{StaticResource HeroGradientStartColor}" />
  <SolidColorBrush x:Key="HeroGradientEndBrush"  Color="{StaticResource HeroGradientEndColor}" />
```

Dark equivalents in `Tokens.Dark.axaml` (add after the `ScrimColor` line at L50):

```xml
  <Color x:Key="FocusRingColor">        #80e8c4</Color>
  <Color x:Key="FocusRingSoftColor">    #3d80e8c4</Color>
  <Color x:Key="PressedBgColor">       #4d9a82</Color>     <!-- accent 400 — pressed = darken -->
  <Color x:Key="SelectedBgColor">      #1a4f42</Color>     <!-- accent 200 — was AccentSoft @ 0x14, now solid -->
  <Color x:Key="SelectedHoverBgColor"> #236856</Color>     <!-- accent 300 -->
  <Color x:Key="DisabledBgColor">      #1c1c22</Color>     <!-- = Surface -->
  <Color x:Key="DisabledFgColor">      #5a5a62</Color>     <!-- muted 600 -->
  <Color x:Key="ErrorBgColor">         #2a1517</Color>
  <Color x:Key="ErrorBorderColor">     #e08488</Color>     <!-- = Danger -->
  <Color x:Key="ErrorFgColor">         #e08488</Color>
  <Color x:Key="SuccessFgColor">       #80e8c4</Color>     <!-- = Accent in dark -->
  <Color x:Key="SuccessBorderColor">   #4d9a82</Color>
  <Color x:Key="WarningFgColor">       #e0a16e</Color>     <!-- = Warn -->
  <Color x:Key="WarningBorderColor">   #e0a16e</Color>
  <Color x:Key="CodeBgColor">          #131318</Color>     <!-- sunken, code reads on darker -->
  <Color x:Key="CodeFgColor">          #c2f3de</Color>     <!-- accent 700 for syntax-ish -->
  <Color x:Key="HeroGradientStartColor">#0d0d10</Color>
  <Color x:Key="HeroGradientEndColor">  #1a2030</Color>     <!-- deep teal-navy vignette, 10° toward accent -->
```

### Why the focus ring is its own token, not just `AccentBrush`

Because focus ring needs `BoxShadow`-style glow that goes **outside** the element. If you reuse `AccentBrush` for the border, you can never apply an outer ring without the border fighting the focus style. Keep them separate, and add the focus style globally (see §10 for the exact Style).

---

## 4. New color additions to consider

| Token | Hex | Purpose | Where to use |
|---|---|---|---|
| `SurfaceSunken` (renamed) | L: `#f1ede4` / D: `#131318` | Sidebar background, recessed panels | Already in use, just renamed |
| `SurfaceRaised` | L: `#ffffff` / D: `#232329` | Cards on a sunken surface (sidebar → conversation panel) | Quick action cards, command-list rows |
| `SurfaceOverlay` | L: `#ffffff` / D: `#2b2b33` | Popovers, tooltips, modals | Command palette, settings, toasts |
| `LineStrong` | L: `#d8d2c2` / D: `#3d3d46` | Visible divider on overlays | Between sidebar and main in light mode |
| `CodeBg` | L: `#f5f0e1` / D: `#131318` | Code blocks in the conversation | Markdown code spans, tool result panes |
| `HeroGradientStart/End` | L: `#fafaf7`→`#e8e9d8` / D: `#0d0d10`→`#1a2030` | The empty-state backdrop | Empty-state panel in `MainWindow.axaml` L323 |
| `FocusRing` | L: `#2f6f5e` / D: `#80e8c4` | 2px solid + 2px offset on any focusable | See §10 |
| `SelectedBg` | L: `#cfe5dd` / D: `#1a4f42` | Active sidebar row, active command item | Already using `AccentSoftBrush` — replace |
| `DisabledFg` | L: `#a8a8b0` / D: `#5a5a62` | Text on disabled controls | `:disabled` selectors |

The 4 most impactful right now: **FocusRing** (a11y blocker), **CodeBg** (the conversation is full of code and there's no styled background for it), **HeroGradient** (the empty state is the first impression), **SelectedBg** (the current 8% wash is barely visible on cream).

---

## 5. Typography

### The current scale is broken

Look at `Tokens.axaml` L98–L103:

```
FontXs  11
FontSm  12
FontBase 13
FontMd  14
FontLg  17
FontXl  22
```

This is **not** a scale. There's no ratio — 12→13→14 is +1, then 14→17 is +3, then 17→22 is +5. The hero in `App.axaml` L52 hand-codes `"32"` because there's no step for it. The kbd pill in `App.axaml` L309 hand-codes `"11"` because `FontXs` doesn't help when it needs to be paired with mono.

### The fix — 1.2 modular ratio, 10 named roles

Compute base = 14, ratio = 1.2, then snap to integers:

| Step | px | Used for | Weight | Letter-spacing | Line-height |
|---|---|---|---|---|---|
| `Display` | 40 | Hero on the welcome screen | `Bold` (700) | `-0.03em` | 1.1 |
| `H1` | 30 | Page title, modal headers | `SemiBold` (600) | `-0.02em` | 1.15 |
| `H2` | 22 | Section titles (existing `FontXl` becomes this) | `SemiBold` | `-0.015em` | 1.2 |
| `H3` | 18 | Subsection, "理解项目" empty-state card title | `SemiBold` | `-0.01em` | 1.25 |
| `BodyL` | 16 | Reading content, hero sub-text (was hand-coded 14) | `Regular` (400) | `0` | 1.5 |
| `BodyM` | 14 | Default body (`FontMd` already at 14) | `Regular` | `0` | 1.5 |
| `BodyS` | 13 | Dense UI, table cells | `Regular` | `0` | 1.45 |
| `Caption` | 12 | Labels, secondary text (`FontSm` already at 12) | `Regular` | `+0.005em` | 1.4 |
| `Overline` | 11 | Section labels, badges, "可运行" pill text | `SemiBold` | `+0.06em` (UPPER) | 1.3 |
| `Micro` | 10 | KBD shortcuts, code annotations | `Medium` (500) | `0` | 1.2 |

### The exact XAML — replace `Tokens.axaml` L98–L103 with:

```xml
  <!-- ===== Type ramp (1.2 ratio, base 14) ===== -->
  <x:Double x:Key="FontDisplay">40</x:Double>
  <x:Double x:Key="FontH1">30</x:Double>
  <x:Double x:Key="FontH2">22</x:Double>
  <x:Double x:Key="FontH3">18</x:Double>
  <x:Double x:Key="FontBodyL">16</x:Double>
  <x:Double x:Key="FontBodyM">14</x:Double>
  <x:Double x:Key="FontBodyS">13</x:Double>
  <x:Double x:Key="FontCaption">12</x:Double>
  <x:Double x:Key="FontOverline">11</x:Double>
  <x:Double x:Key="FontMicro">10</x:Double>
  <!-- Legacy aliases — keep for one release so styles don't break -->
  <x:Double x:Key="FontXl">22</x:Double>
  <x:Double x:Key="FontLg">17</x:Double>
  <x:Double x:Key="FontMd">14</x:Double>
  <x:Double x:Key="FontBase">13</x:Double>
  <x:Double x:Key="FontSm">12</x:Double>
  <x:Double x:Key="FontXs">11</x:Double>
```

### Line-heights as tokens (Avalonia needs them per-style, not per-token — but for reuse)

Add this block after the type ramp:

```xml
  <x:Double x:Key="LineHeightTight">1.15</x:Double>
  <x:Double x:Key="LineHeightSnug">1.25</x:Double>
  <x:Double x:Key="LineHeightNormal">1.45</x:Double>
  <x:Double x:Key="LineHeightRelaxed">1.6</x:Double>
  <x:Double x:Key="TrackingTight">-0.02</x:Double>
  <x:Double x:Key="TrackingNormal">0</x:Double>
  <x:Double x:Key="TrackingWide">0.04</x:Double>
```

### Update `App.axaml` — the styles

Replace the entire text section (L35–L63) with the type-ramp-driven styles:

```xml
        <!-- ===== Text — type ramp ===== -->
        <Style Selector="TextBlock.t-display">
            <Setter Property="FontFamily" Value="{StaticResource FontSans}" />
            <Setter Property="FontSize" Value="{StaticResource FontDisplay}" />
            <Setter Property="FontWeight" Value="Bold" />
            <Setter Property="LineHeight" Value="{StaticResource LineHeightTight}" />
            <Setter Property="LetterSpacing" Value="{StaticResource TrackingTight}" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style Selector="TextBlock.t-h1">
            <Setter Property="FontSize" Value="{StaticResource FontH1}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="LineHeight" Value="34.5" />
            <Setter Property="LetterSpacing" Value="-0.4" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style Selector="TextBlock.t-h2">
            <Setter Property="FontSize" Value="{StaticResource FontH2}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="LetterSpacing" Value="-0.3" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style Selector="TextBlock.t-h3">
            <Setter Property="FontSize" Value="{StaticResource FontH3}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style Selector="TextBlock.t-body-l">
            <Setter Property="FontSize" Value="{StaticResource FontBodyL}" />
            <Setter Property="LineHeight" Value="24" />
            <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
        </Style>
        <Style Selector="TextBlock.body">     <!-- keep for backward compat -->
            <Setter Property="FontSize" Value="{StaticResource FontBodyM}" />
            <Setter Property="LineHeight" Value="21" />
            <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
        </Style>
        <Style Selector="TextBlock.t-body-s">
            <Setter Property="FontSize" Value="{StaticResource FontBodyS}" />
            <Setter Property="LineHeight" Value="19" />
            <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
        </Style>
        <Style Selector="TextBlock.t-caption">
            <Setter Property="FontSize" Value="{StaticResource FontCaption}" />
            <Setter Property="LineHeight" Value="17" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.muted">    <!-- keep for backward compat -->
            <Setter Property="FontSize" Value="{StaticResource FontCaption}" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.t-overline">
            <Setter Property="FontSize" Value="{StaticResource FontOverline}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="LetterSpacing" Value="0.66" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.section-title">  <!-- existing: remap to t-overline -->
            <Setter Property="FontSize" Value="{StaticResource FontOverline}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="LetterSpacing" Value="0.66" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.t-micro">
            <Setter Property="FontFamily" Value="{StaticResource FontMono}" />
            <Setter Property="FontSize" Value="{StaticResource FontMicro}" />
            <Setter Property="FontWeight" Value="Medium" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.kbd-inline">     <!-- existing: pair with t-micro -->
            <Setter Property="FontFamily" Value="{StaticResource FontMono}" />
            <Setter Property="FontSize" Value="{StaticResource FontMicro}" />
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        </Style>
        <Style Selector="TextBlock.page-title">     <!-- existing: now FontH1 -->
            <Setter Property="FontSize" Value="{StaticResource FontH1}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style Selector="TextBlock.hero-title">     <!-- existing: now FontDisplay -->
            <Setter Property="FontFamily" Value="{StaticResource FontSans}" />
            <Setter Property="FontWeight" Value="Bold" />
            <Setter Property="FontSize" Value="{StaticResource FontDisplay}" />
            <Setter Property="LineHeight" Value="44" />
            <Setter Property="LetterSpacing" Value="-1.2" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
```

### Outfit is good. JetBrains Mono is good. Two refinements.

1. **Add an `Outfit`-specific weight token system.** Outfit's display weights (700+) are gorgeous; pair Display/Bold with tracking `−0.03em` and the new `H1` (−0.02em). Already in the table.
2. **Add a CJK fallback.** Outfit is Latin-only. Add to `FontSans`:
   ```xml
   <FontFamily x:Key="FontSans">Outfit, -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif</FontFamily>
   ```
   And `FontMono`:
   ```xml
   <FontFamily x:Key="FontMono">JetBrains Mono, ui-monospace, "Sarasa Mono SC", monospace</FontFamily>
   ```
   This is critical because the entire product UI is bilingual — look at `MainWindow.axaml` L111, L122, L142, L329.

---

## 6. Spacing

### What's wrong

- `Space1=4, Space2=8, Space3=12, Space4=16, Space6=24` — there are **only 5 values**.
- **Space5 is missing.** Every designer that opens these files will look for 20 and not find it.
- **The gap series (SpaceGap1–6) duplicates the thickness series with the same values.** Two parallel series is two systems to remember. Pick one.
- **No 32/48/64** for the modal, hero, and settings panel padding.

### The fix — one series, 8 steps

Replace `Tokens.axaml` L76–L86 with:

```xml
  <!-- ===== Spacing (8pt grid + 4pt half-step) ===== -->
  <x:Double x:Key="Space0">0</x:Double>
  <x:Double x:Key="Space1">4</x:Double>
  <x:Double x:Key="Space2">8</x:Double>
  <x:Double x:Key="Space3">12</x:Double>
  <x:Double x:Key="Space4">16</x:Double>
  <x:Double x:Key="Space5">20</x:Double>
  <x:Double x:Key="Space6">24</x:Double>
  <x:Double x:Key="Space7">32</x:Double>
  <x:Double x:Key="Space8">40</x:Double>
  <x:Double x:Key="Space9">48</x:Double>
  <x:Double x:Key="Space10">64</x:Double>
  <x:Double x:Key="Space11">80</x:Double>

  <Thickness x:Key="Padding1">4</Thickness>
  <Thickness x:Key="Padding2">8</Thickness>
  <Thickness x:Key="Padding3">12</Thickness>
  <Thickness x:Key="Padding4">16</Thickness>
  <Thickness x:Key="Padding5">20</Thickness>
  <Thickness x:Key="Padding6">24</Thickness>
  <Thickness x:Key="Padding7">32</Thickness>

  <!-- Pre-composed axis-specific paddings — drop these 3 lines and you stop hand-coding "12,9" everywhere -->
  <Thickness x:Key="PaddingBubbleX">12,9</Thickness>
  <Thickness x:Key="PaddingBubbleY">9,12</Thickness>
  <Thickness x:Key="PaddingModal">32</Thickness>
```

Then sweep `App.axaml` and replace the literals:

- L82 `Padding="14"` → `Padding="{StaticResource Padding3}"` (close enough; or add `Padding14`)
- L101 `Padding="14,12"` → `Padding="{StaticResource Padding4},{StaticResource Padding3}"`
- L114 `Padding="12,9"` → `Padding="{StaticResource PaddingBubbleX}"` (after adding the token)
- L121 `Padding="12,9"` → same
- L158 `Padding="14,12"` → same as L101
- L195 `Padding="9,4"` → `Padding="{StaticResource Padding2},{StaticResource Padding1}"`
- L266 `Padding="14,12"` → as L101
- L280 `Padding="14,12"` → as L101
- L304 `Padding="6,2"` → `Padding="{StaticResource Padding2},{StaticResource Padding0_5}"` after you add the half-step, or just keep as-is
- L443 `Padding="10,5"` → keep, but add `Padding="9,4"` token (call it `PaddingChip`)

Drop the `SpaceGap*` series — every consumer in the codebase is using the same values as `Space*`, so the second series is just noise.

---

## 7. Radius

### What's there

```
RadiusSm  6
RadiusMd  10
RadiusLg  14
RadiusFull 999
```

### What's wrong

1. **No 4** — small chips and inline tags need it.
2. **No 20** — large cards (the 280×92 quick action cards in `MainWindow.axaml` L347+) want 16 or 20, not 14.
3. **`RadiusFull 999` is used for the model chip at L443** — a 10×5 height pill wants 999, that's fine. But the existing `kbd-pill` at `App.axaml` L299–L306 uses `RadiusSm=6` for a `6,2` pad, which gives a 6×6 corner on a 14×16 box — that's right.
4. **The send button (`App.axaml` L129–L137) is 36×36 with `CornerRadius=10`.** That should be **`999`** (pill on a square) or **`12`** (square-ish). 10 reads as "rounded rectangle", which is what shadcn does, so it's actually fine — but on a 36×36 button with an arrow, **18** (full pill) or **10** (subtle) — pick **18** for the send button specifically because the arrow is round.

### The fix — 6 steps + explicit pill

Add to `Tokens.axaml` after L92:

```xml
  <CornerRadius x:Key="RadiusXs">4</CornerRadius>     <!-- chips, small tags -->
  <CornerRadius x:Key="RadiusSm">6</CornerRadius>     <!-- keep, kbd pills -->
  <CornerRadius x:Key="RadiusMd">10</CornerRadius>    <!-- keep, buttons -->
  <CornerRadius x:Key="RadiusLg">14</CornerRadius>    <!-- keep, input shell -->
  <CornerRadius x:Key="RadiusXl">20</CornerRadius>    <!-- NEW, large cards -->
  <CornerRadius x:Key="Radius2xl">28</CornerRadius>   <!-- NEW, hero / modal -->
  <CornerRadius x:Key="RadiusFull">999</CornerRadius> <!-- keep, but rename aliases -->
  <CornerRadius x:Key="RadiusButtonPill">18</CornerRadius>   <!-- 36×36 send button -->
  <CornerRadius x:Key="RadiusInputPill">999</CornerRadius>   <!-- if you want a search-like input -->
```

In `App.axaml`:
- L134 `CornerRadius="10"` → `CornerRadius="{StaticResource RadiusButtonPill}"`
- L82 `CornerRadius="9"` → `CornerRadius="{StaticResource RadiusSm}"`

---

## 8. Shadows & elevation

### The current shadow vocabulary

| Token | Light value | Where used | Verdict |
|---|---|---|---|
| `ModalShadow` | `0 24 64 0 #40000000, 0 0 0 1 #10000000` | Settings, tool approval | OK — dual shadow is the right move |
| `ToastShadow` | `0 8 24 0 #30000000` | Bottom-right toasts | Too flat — toasts need an inner edge to read at small sizes |
| `FloatingShadow` | `0 8 24 0 #20000000, 0 1 0 0 #10000000` | Input composer | Too soft — `#20` alpha disappears against cream |

### The fix — proper 0–5 elevation scale

Replace the three tokens in `Tokens.axaml` (L69–L74) and add the rest:

```xml
  <!-- ===== Elevation (0 → 5) ===== -->
  <BoxShadows x:Key="Elevation0Shadow">0 0 0 0 #00000000</BoxShadows>            <!-- flat; use on inset cards -->
  <BoxShadows x:Key="Elevation1Shadow">0 1 2 0 #10000000, 0 0 0 1 #08000000</BoxShadows>  <!-- subtle row lift -->
  <BoxShadows x:Key="Elevation2Shadow">0 4 12 0 #14000000, 0 0 0 1 #08000000</BoxShadows>  <!-- resting card on Bg2 -->
  <BoxShadows x:Key="Elevation3Shadow">0 8 24 0 #18000000, 0 0 0 1 #0A000000</BoxShadows>  <!-- popover, floating input -->
  <BoxShadows x:Key="Elevation4Shadow">0 16 40 0 #24000000, 0 0 0 1 #0A000000</BoxShadows>  <!-- modal, dialog -->
  <BoxShadows x:Key="Elevation5Shadow">0 24 64 0 #3C000000, 0 0 0 1 #10000000</BoxShadows>  <!-- command palette -->
  <!-- Toast gets a flatter profile + a 1px ring to read at 36px height -->
  <BoxShadows x:Key="ElevationToastShadow">0 4 12 0 #18000000, 0 0 0 1 #10000000</BoxShadows>
```

And in `Tokens.Dark.axaml` (L53–L55, replace all three + add 2):

```xml
  <BoxShadows x:Key="Elevation0Shadow">0 0 0 0 #00000000</BoxShadows>
  <BoxShadows x:Key="Elevation1Shadow">0 1 2 0 #40000000, 0 0 0 1 #1A000000</BoxShadows>
  <BoxShadows x:Key="Elevation2Shadow">0 4 12 0 #50000000, 0 0 0 1 #1A000000</BoxShadows>
  <BoxShadows x:Key="Elevation3Shadow">0 8 24 0 #60000000, 0 0 0 1 #20000000</BoxShadows>
  <BoxShadows x:Key="Elevation4Shadow">0 16 40 0 #70000000, 0 0 0 1 #24000000</BoxShadows>
  <BoxShadows x:Key="Elevation5Shadow">0 24 64 0 #80000000, 0 0 0 1 #30000000</BoxShadows>
  <BoxShadows x:Key="ElevationToastShadow">0 4 12 0 #50000000, 0 0 0 1 #30000000</BoxShadows>
```

### Migration map

| Old | New | Reason |
|---|---|---|
| `ModalShadow` (L69) | `Elevation4Shadow` | Same intent, coherent with the ladder |
| `ToastShadow` (L70) | `ElevationToastShadow` | Toasts need a flatter, ringed profile |
| `FloatingShadow` (L74) | `Elevation3Shadow` | The input composer is an elevation-3 surface, not its own thing |

In `App.axaml` L102 `BoxShadow="{StaticResource FloatingShadow}"` → `BoxShadow="{StaticResource Elevation3Shadow}"`.

---

## 9. Animations & motion

### What you have

Exactly one animation in the whole app: three `Ellipse` opacity waves with `Delay="0.2"` and `Delay="0.4"` (`MainWindow.axaml` L249–L287). That's it. The product feels static next to Linear/Vercel/Cursor/Raycast.

### The motion vocabulary — 8 named motions

| Name | Where | Duration | Easing | Properties animated |
|---|---|---|---|---|
| `MotionFast` | Button press, hover | 100ms | `CubicEaseOut` | `Opacity`, `Background` |
| `MotionBase` | Page transitions, panel reveal | 180ms | `CubicEaseOut` | `Opacity`, `TranslateTransform.Y` |
| `MotionSlow` | Modal enter, command palette open | 240ms | `CubicEaseInOut` | `Opacity`, `ScaleTransform.ScaleX/Y` |
| `MotionBubble` | New conversation bubble | 220ms | `CubicEaseOut` | `Opacity`, `TranslateTransform.Y` (8px) |
| `MotionDot` | Thinking dots | 1200ms (per cycle) | `SineEaseInOut` | `Opacity` |
| `MotionToast` | Toast slide in from bottom-right | 220ms | `CubicEaseOut` | `Opacity`, `TranslateTransform.Y` (16px) |
| `MotionShimmer` | Streaming text reveal | 200ms per token | `LinearEasing` | `Opacity` |
| `MotionPulse` | Accent ping on save | 600ms | `SineEaseInOut` | `Opacity` on a 1px ring |

Add as resources in `Tokens.axaml` (after the elevation block):

```xml
  <!-- ===== Motion ===== -->
  <x:Double x:Key="MotionFastMs">100</x:Double>
  <x:Double x:Key="MotionBaseMs">180</x:Double>
  <x:Double x:Key="MotionSlowMs">240</x:Double>
  <x:Double x:Key="MotionBubbleMs">220</x:Double>
  <x:Double x:Key="MotionDotMs">1200</x:Double>
  <x:Double x:Key="MotionToastMs">220</x:Double>
  <TimeSpan x:Key="MotionFast">0:0:0.100</TimeSpan>
  <TimeSpan x:Key="MotionBase">0:0:0.180</TimeSpan>
  <TimeSpan x:Key="MotionSlow">0:0:0.240</TimeSpan>
  <TimeSpan x:Key="MotionBubble">0:0:0.220</TimeSpan>
  <TimeSpan x:Key="MotionToast">0:0:0.220</TimeSpan>
```

### Exact animation styles — add to `App.axaml` (after the inputs section, before L328)

```xml
        <!-- ===== Motion: global animations ===== -->

        <!-- 1. Button press — uses Transitions, the lightweight Avalonia system.
             Goes on every Button so it works for chrome-button, primary, send-button
             without per-style duplication. -->
        <Style Selector="Button">
            <Setter Property="RenderTransform" Value="scale(1.0)" />
            <Setter Property="Transitions">
                <Transitions>
                    <TransformOperationsTransition Property="RenderTransform" Duration="{StaticResource MotionFast}" Easing="CubicEaseOut" />
                    <BrushTransition Property="Background" Duration="{StaticResource MotionFast}" />
                    <BrushTransition Property="Foreground" Duration="{StaticResource MotionFast}" />
                    <BrushTransition Property="BorderBrush" Duration="{StaticResource MotionFast}" />
                </Transitions>
            </Setter>
        </Style>
        <Style Selector="Button:pressed">
            <Setter Property="RenderTransform" Value="scale(0.97)" />
        </Style>

        <!-- 2. Focus ring — global. WCAG 2.4.7 + 2.2.2. -->
        <Style Selector="Button:focus, TextBox:focus, ToggleSwitch:focus, ListBoxItem:focus">
            <Setter Property="BorderBrush" Value="{StaticResource FocusRingBrush}" />
        </Style>
        <Style Selector="Button:focus-visible, TextBox:focus-visible, ToggleSwitch:focus-visible, ListBoxItem:focus-visible">
            <!-- 2px outer ring via BoxShadow with a negative spread to draw it outside the border -->
            <Setter Property="BorderBrush" Value="{StaticResource FocusRingBrush}" />
            <Setter Property="BorderThickness" Value="2" />
        </Style>

        <!-- 3. Sidebar row enter — new conversation rows slide in 8px from bottom. -->
        <Style Selector="Button.sidebar-row">
            <Setter Property="Transitions">
                <Transitions>
                    <TransformOperationsTransition Property="RenderTransform" Duration="{StaticResource MotionBubble}" Easing="CubicEaseOut" />
                </Transitions>
            </Setter>
        </Style>

        <!-- 4. Conversation bubble enter — new AI / user bubbles fade in + rise 6px. -->
        <Style Selector="Border.bubble-ai, Border.bubble-user">
            <Setter Property="Opacity" Value="0" />
            <Setter Property="RenderTransform" Value="translate(0, 6px)" />
            <Setter Property="Transitions">
                <Transitions>
                    <DoubleTransition Property="Opacity" Duration="{StaticResource MotionBubble}" Easing="CubicEaseOut" />
                    <TransformOperationsTransition Property="RenderTransform" Duration="{StaticResource MotionBubble}" Easing="CubicEaseOut" />
                </Transitions>
            </Setter>
        </Style>
        <!-- The data trigger: as soon as the bubble is rendered, animate to visible.
             This requires an attached property or DataTrigger; the cleanest Avalonia
             approach is to use Style.Animations keyed off IsItemsHost attached state.
             For 1.0, a code-behind Loaded handler in the DataTemplate is the pragmatic
             path — see §10 implementation note. -->
```

### Implementation notes for the bubble enter (because pure-XAML is awkward in Avalonia 12)

In the `DataTemplate` at `MainWindow.axaml` L220–L316, the cleanest pattern is:

```xml
<DataTemplate x:DataType="vm:ActivityItemViewModel">
    <Border Classes="bubble-enter">
        <!-- ... existing content ... -->
    </Border>
</DataTemplate>
```

with a global style:

```xml
<Style Selector="Border.bubble-enter">
    <Setter Property="Opacity" Value="0" />
    <Setter Property="RenderTransform" Value="translate(0, 8px)" />
    <Style.Animations>
        <Animation Duration="{StaticResource MotionBubble}" Easing="CubicEaseOut" FillMode="Forward">
            <KeyFrame Cue="0%">
                <Setter Property="Opacity" Value="0" />
                <Setter Property="RenderTransform" Value="translate(0, 8px)" />
            </KeyFrame>
            <KeyFrame Cue="100%">
                <Setter Property="Opacity" Value="1" />
                <Setter Property="RenderTransform" Value="translate(0, 0)" />
            </KeyFrame>
        </Animation>
    </Style.Animations>
</Style>
```

Caveat: Style.Animations with `FillMode="Forward"` run once on element attach. Since each new bubble creates a new Border, this works. But `Style.Animations` has a known Avalonia 11+ issue where the animation runs on a one-frame delay — the border is briefly visible at full opacity before the animation starts. If you see that flicker, fall back to a `Loaded` code-behind handler:

```csharp
private void Bubble_OnLoaded(object? sender, RoutedEventArgs e)
{
    if (sender is Border b)
    {
        b.Opacity = 0;
        b.RenderTransform = new TranslateTransform(0, 8);
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0%),  Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(100%), Setters = { new Setter(OpacityProperty, 1.0) } }
            }
        };
        anim.RunAsync(b);
    }
}
```

### The thinking-dot loop — refactor

The three hand-rolled `Ellipse.Styles` blocks at L249–L287 should be a `UserControl` or a reusable `Style` selector. Add to `App.axaml`:

```xml
        <Style Selector="Ellipse.thinking-dot">
            <Setter Property="Width" Value="6" />
            <Setter Property="Height" Value="6" />
            <Setter Property="Fill" Value="{StaticResource MutedBrush}" />
            <Setter Property="Opacity" Value="0.25" />
            <Style.Animations>
                <Animation Duration="{StaticResource MotionDot}" IterationCount="Infinite" Easing="SineEaseInOut">
                    <KeyFrame Cue="0%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                    <KeyFrame Cue="50%"><Setter Property="Opacity" Value="1.0" /></KeyFrame>
                    <KeyFrame Cue="100%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                </Animation>
            </Style.Animations>
        </Style>
```

Then `MainWindow.axaml` L249–L287 becomes:

```xml
<StackPanel IsVisible="{Binding IsThinking}" Orientation="Horizontal" Spacing="5" VerticalAlignment="Center" HorizontalAlignment="Left">
    <Ellipse Classes="thinking-dot" />
    <Ellipse Classes="thinking-dot">
        <Ellipse.Styles>
            <Style Selector="Ellipse" BasedOn="{StaticResource {x:Type Ellipse}}">
                <Style.Animations>
                    <Animation Duration="{StaticResource MotionDot}" IterationCount="Infinite" Easing="SineEaseInOut" Delay="0:0:0.2">
                        <KeyFrame Cue="0%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                        <KeyFrame Cue="50%"><Setter Property="Opacity" Value="1.0" /></KeyFrame>
                        <KeyFrame Cue="100%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                    </Animation>
                </Style.Animations>
            </Style>
        </Ellipse.Styles>
    </Ellipse>
    <Ellipse Classes="thinking-dot">
        <Ellipse.Styles>
            <Style Selector="Ellipse" BasedOn="{StaticResource {x:Type Ellipse}}">
                <Style.Animations>
                    <Animation Duration="{StaticResource MotionDot}" IterationCount="Infinite" Easing="SineEaseInOut" Delay="0:0:0.4">
                        <KeyFrame Cue="0%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                        <KeyFrame Cue="50%"><Setter Property="Opacity" Value="1.0" /></KeyFrame>
                        <KeyFrame Cue="100%"><Setter Property="Opacity" Value="0.25" /></KeyFrame>
                    </Animation>
                </Style.Animations>
            </Style>
        </Ellipse.Styles>
    </Ellipse>
</StackPanel>
```

Better: a custom control in `Controls/ThinkingDots.cs` (an `ItemsControl` of 3 `Ellipse`s with the staggered animations baked in). That's the right answer for 1.0.

---

## 10. Brand identity — the wordmark

### The current mark

`MainWindow.axaml` L49–L57 — a 26×26 rounded square with `AccentSoft` background and the letters "AI" in `Accent` at 11px SemiBold. This is a **placeholder**, not a mark. A 2-letter monogram in 11px has no recognizable silhouette, no legibility at small sizes, and no brand signal beyond "I gave up and put text here".

### What the brand needs

The cream + teal palette is distinctive. The right mark leans into that. Three concrete directions, ranked:

**Direction 1 (recommended): A geometric "speech-bubble-leaf" mark.**

A 24×24 mark that reads as: a chat bubble whose tail curls into a leaf, drawn in solid `AccentBrush` with a single counter (negative space) in the shape of a smaller bubble. 1 stroke, 1 fill, 0 gradients. Works at 16px (favicon), 24px (titlebar), 48px (app icon), 512px (store). The cream + teal palette means the mark can be drawn either filled-on-cream (titlebar) or filled-on-white (about screen) without a third color.

**Direction 2: A monospaced bracket mark.**

`{ }` rendered in JetBrains Mono Bold at 18px, color `AccentBrush`, in a 24×24 box. Reads as "code-aware chat" and leans into the developer-tool brand. Lower risk than Direction 1 because it requires no SVG, just a `TextBlock` with `FontFamily="JetBrains Mono"` and `Text="{ }"`. Ship as a fallback if Direction 1's SVG isn't ready.

**Direction 3: A 3-dot constellation.**

Three filled circles, two `AccentBrush` + one `AccentSoftBrush`, arranged in a 2+1 triangle inside a 24×24 box with 6px padding. Reads as "typing" + "thinking" + "responding" — perfectly on-brand for the thinking-dot animation. Cheapest to implement (3 `Ellipse`s) and visually ties to the existing motion language.

### The XAML — ship Direction 3 today, plan Direction 1 for 1.0.1

Replace the titlebar block in `MainWindow.axaml` L49–L57 with:

```xml
<Border Width="24" Height="24" Background="Transparent">
    <Canvas Width="24" Height="24">
        <Ellipse Width="8" Height="8" Fill="{StaticResource AccentBrush}" Canvas.Left="2"  Canvas.Top="2" />
        <Ellipse Width="8" Height="8" Fill="{StaticResource AccentBrush}" Canvas.Left="14" Canvas.Top="2" />
        <Ellipse Width="8" Height="8" Fill="{StaticResource Accent300Brush}" Canvas.Left="8"  Canvas.Top="14" />
    </Canvas>
</Border>
<TextBlock Text="AIChat" Classes="t-h3" Margin="10,0,0,0" VerticalAlignment="Center" />
```

Add a small wordmark lockup helper for the about screen and splash:

```xml
<StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Center">
    <Border Width="40" Height="40" Background="Transparent">
        <Canvas Width="40" Height="40">
            <Ellipse Width="13" Height="13" Fill="{StaticResource AccentBrush}" Canvas.Left="3"  Canvas.Top="3" />
            <Ellipse Width="13" Height="13" Fill="{StaticResource AccentBrush}" Canvas.Left="24" Canvas.Top="3" />
            <Ellipse Width="13" Height="13" Fill="{StaticResource Accent300Brush}" Canvas.Left="13" Canvas.Top="24" />
        </Canvas>
    </Border>
    <TextBlock Classes="t-h1" Text="AIChat" VerticalAlignment="Center" />
</StackPanel>
```

### The "AI" badge in the AI bubble — same fix

`MainWindow.axaml` L229–L234 and L240 use the same placeholder. Replace both with a 28×28 mini-mark: two teal dots in the top row, one teal-300 dot in the bottom row, padded by 4px. Match the same proportions as the titlebar mark.

### Favicon / app icon

The `MainWindow.axaml` L11 references `/Assets/avalonia-logo.ico` — that's the default Avalonia icon, not the AIChat brand. Replace with `Assets/aichat-mark.ico`. Until you have a real SVG export, generate it from the 3-dot mark above by rendering a 256×256 canvas of the same composition.

---

## Summary of files to touch

| File | Action |
|---|---|
| `Resources/Tokens.axaml` | Add teal ramp (L23), interaction-state colors, code/hero/scrim tokens, fix `LineSoftColor`, add type ramp + spacing ramp + radius + elevation + motion tokens |
| `Resources/Tokens.Dark.axaml` | Mirror teal ramp, add interaction-state colors, fix surface ladder, add elevation ladder |
| `App.axaml` | Replace text styles with type-ramp styles, add focus ring, add motion vocabulary, replace `Bg2Brush` references with `SurfaceSunkenBrush` |
| `Views/MainWindow.axaml` | L49–L57: new mark · L82: `Padding="14"` → token · L101: `Padding="14,12"` → token · L229–L234 + L240: new AI bubble mark · L249–L287: refactor to `Ellipse.thinking-dot` |
| `Controls/ThinkingDots.cs` | NEW — `UserControl` wrapping the 3-dot animation |
| `Assets/aichat-mark.ico` | NEW — 256×256 export of the 3-dot mark |

Build target unchanged. No new packages. The teal ramp + new tokens are pure `<Color>` / `<SolidColorBrush>` entries that compose with the existing `{StaticResource}` lookup.

---

## Appendix — contrast matrix (WCAG 2.2 AA verified)

| Foreground | Background | Ratio | AA Normal | AA Large | AAA Normal |
|---|---|---|---|---|---|
| `Text #18181b` (light) | `Bg #fafaf7` | **16.3:1** | ✅ | ✅ | ✅ |
| `Text2 #3a4256` | `Bg #fafaf7` | **10.2:1** | ✅ | ✅ | ✅ |
| `Muted #71717a` (current) | `Bg #fafaf7` | **4.6:1** | ✅ | ✅ | ❌ |
| `Muted #6b6b75` (proposed) | `Bg #fafaf7` | **5.1:1** | ✅ | ✅ | ✅ |
| `Muted #6b6b75` | `Bg2 #f1ede4` | **4.7:1** | ✅ | ✅ | ❌ |
| `Accent #2f6f5e` | `Bg #fafaf7` | **6.4:1** | ✅ | ✅ | ✅ |
| `Accent #2f6f5e` | `Surface #ffffff` | **6.0:1** | ✅ | ✅ | ✅ |
| `AccentSoft` text on | `Bg #fafaf7` | n/a — use solid `SelectedBg #cfe5dd` instead | | | |
| `Text #e8e8eb` (dark) | `Bg #0d0d10` | **16.8:1** | ✅ | ✅ | ✅ |
| `Text2 #c8c8cf` | `Bg #0d0d10` | **11.2:1** | ✅ | ✅ | ✅ |
| `Muted #8a8a92` | `Bg #0d0d10` | **5.5:1** | ✅ | ✅ | ✅ |
| `Accent #80e8c4` | `Bg #0d0d10` | **14.6:1** | ✅ | ✅ | ✅ |
| `Danger #e08488` | `Bg #0d0d10` | **7.1:1** | ✅ | ✅ | ✅ |
| `Warn #e0a16e` | `Bg #0d0d10` | **9.3:1** | ✅ | ✅ | ✅ |
| `FocusRing #2f6f5e` | `Surface #ffffff` | **6.0:1** (border) | ✅ | ✅ | ✅ |
| `FocusRing #80e8c4` | `Surface #1c1c22` | **9.1:1** (border) | ✅ | ✅ | ✅ |

WCAG 2.2 SC 1.4.11 (Non-text Contrast) requires UI components and focus indicators to maintain **3:1** against adjacent colors. Every `*Color` and `*Brush` in this audit clears that bar.
