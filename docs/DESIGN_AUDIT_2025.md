# AIChat Design Audit & Redesign Prescription

**Date:** 2025-01
**Scope:** `src/AIChat.App.Avalonia/` — Tokens, App styles, MainWindow
**Reference apps:** Claude.ai, ChatGPT, Cursor, Raycast AI, v0, Perplexity, Continue.dev/Cline, Linear, Notion AI

This document prescribes concrete, file-level + line-level changes. Every color, spacing, radius, and font value goes through a token. No new fonts (Outfit + JetBrains Mono only). No backend changes. 2-col layout stays.

---

## 1. Layout & Information Architecture

**What we have now.** `MainWindow.axaml:99` defines a 2-col grid: sidebar `260` + flex main with `14,4,14,6` margin and `14` column gap. The main column has a 3-row grid (`Auto,*,Auto`) of header / scrollable content / input. The header (`MainWindow.axaml:187–211`) shows a project breadcrumb, hero greeting, sub-greeting, and three corner badges. The status bar at row 3 is 32px with model name + settings link. Window background is transparent with an 18px-rounded inner border.

**What top apps do.**
- **Claude.ai**: 260px sidebar; main has *no top header* on the conversation surface — the chat starts at the top with the first bubble. The composer is the only chrome between the bubbles and the bottom of the window.
- **ChatGPT**: same — main column is just `bubble stack + composer`. No greeting above the chat.
- **Linear**: top-of-main has a thin 44px toolbar with breadcrumb + view-switcher + global search. No hero greeting.
- **Raycast AI**: no sidebar at all; single column with docked conversation.

**Recommendation.**
1. **Remove the hero greeting from the main column** (the `StackPanel` at `MainWindow.axaml:188–199` showing `Greeting` + `SubGreeting`). On a real conversation, it wastes 80px at the top. On the empty state, the existing `StackPanel` at line 323 already provides the hero — let the empty state own the hero, not the always-visible header.
   - **Delete** `MainWindow.axaml:187–211` (the `Page header (compact)` Grid), keep only a thin 36px breadcrumb row: project name + status badges.
   - **Edit** `MainWindow.axaml:184` row height of the header from `Auto` to `36` (fixed).
2. **Push the badges into the status bar** (see §6) so the main column has zero chrome above the scroll area.
3. **Add a sticky conversation search** at the top of the sidebar (Claude, Perplexity, v0 all have this). Add a new row to `MainWindow.axaml:103`:
   ```xml
   <Grid.RowDefinitions>Auto,Auto,*,Auto</Grid.RowDefinitions>
   ```
   Insert between rows 0 and 1:
   ```xml
   <TextBox Grid.Row="1" PlaceholderText="搜索对话…" 
            Background="{StaticResource HoverBgBrush}" BorderThickness="0"
            CornerRadius="{StaticResource RadiusMd}" Padding="10,7" Margin="0,8,0,4"
            Watermark="搜索对话…" />
   ```
   Use a new `Style Selector="TextBox.sidebar-search"` (see §10) so it doesn't inherit the 14px-padding default.
4. **Sidebar 260 → 264**. Linear uses multiples of 4 throughout. The grid `ColumnDefinitions="260,*"` at `MainWindow.axaml:99` becomes `264,*`. This is what the Lottie spacing grid in Linear is based on and is the de-facto chat-app standard.

---

## 2. Typography Hierarchy

**What we have now.** `Tokens.axaml:95–103` defines 6 font sizes (11/12/13/14/17/22) and 2 font families. `App.axaml:49–55` defines `TextBlock.hero-title` with a hardcoded `FontSize="32"`. Letter-spacing is set on `hero-title` to `-0.02`. Section title (`App.axaml:36–40`) is `SemiBold` at 12px.

**What top apps do.**
- **Claude.ai**: body 16px, line-height 1.5, letter-spacing -0.003em. Display headings 24px with -0.02em. Code at 14px.
- **ChatGPT**: body 16px, headings 24–28px, all semibold. Letter-spacing on display text -0.02em.
- **Linear**: body 13px, section labels 12px **Medium (500)** not Semibold, all-caps for very small section headers. Display 18–22px with -0.01em.
- **Cursor**: code 13px, body 14px, very tight letter-spacing on body.

**Recommendation.**

1. **Expand the font-size ramp in `Tokens.axaml:98–103`.** Add display sizes, an in-between, and 15px. Replace lines 98–103 with:
   ```xml
   <x:Double x:Key="Font2xs">10</x:Double>
   <x:Double x:Key="FontXs">11</x:Double>
   <x:Double x:Key="FontSm">12</x:Double>
   <x:Double x:Key="FontBase">13</x:Double>
   <x:Double x:Key="FontMd">14</x:Double>
   <x:Double x:Key="FontLg">15</x:Double>
   <x:Double x:Key="FontXl">17</x:Double>
   <x:Double x:Key="Font2xl">20</x:Double>
   <x:Double x:Key="Font3xl">24</x:Double>
   <x:Double x:Key="FontDisplay">28</x:Double>
   ```
2. **Change `hero-title` from `FontSize="32"` to the `FontDisplay` token** in `App.axaml:52`:
   ```xml
   <Setter Property="FontSize" Value="{StaticResource FontDisplay}" />
   ```
3. **Change the body text size in conversation bubbles from 14 to 15.** `MainWindow.axaml:437` has the composer `FontSize="14"`, and `MainWindow.axaml:198` the sub-greeting `FontSize="13"`. After deleting the hero header (§1), the conversation bubble text is the main reading surface — bump it. Add a new `Style Selector="TextBlock.bubble-text"` in `App.axaml` after line 60:
   ```xml
   <Style Selector="TextBlock.bubble-text">
       <Setter Property="FontSize" Value="{StaticResource FontLg}" />
       <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
       <Setter Property="LineHeight" Value="22" />
   </Style>
   ```
   Then apply `Classes="bubble-text"` to the `MarkdownTextBlock` wrappers in the user/AI bubble templates (`MainWindow.axaml:242, 301`).
4. **Demote `section-title` from SemiBold → Medium.** In `App.axaml:38` change `FontWeight="SemiBold"` to `FontWeight="Medium"`. Linear, Notion, and Claude all use Medium 500 for sidebar section labels.
5. **Add a `letter-spacing` token** to `Tokens.axaml` after line 103:
   ```xml
   <x:Double x:Key="LetterSpacingTight">-0.011</x:Double>
   <x:Double x:Key="LetterSpacingDisplay">-0.022</x:Double>
   ```
   Then change `App.axaml:54` from `LetterSpacing="-0.02"` to `LetterSpacing="{StaticResource LetterSpacingDisplay}"`. Apply `LetterSpacing="{StaticResource LetterSpacingTight}"` to `TextBlock.body` (insert a new setter in `App.axaml:56–59`).
6. **Add FontWeight tokens** to `Tokens.axaml` after the font-size block:
   ```xml
   <x:Double x:Key="FontWeightMedium">500</x:Double>
   <x:Double x:Key="FontWeightSemibold">600</x:Double>
   ```
   (Avalonia `FontWeight` accepts the numeric values via `FontWeight="500"` etc.)

---

## 3. Color & Visual Identity

**What we have now.** `Tokens.axaml:15–39` defines a warm cream palette: Bg `#fafaf7`, Bg2 `#f1ede4`, Line `#e9e3d6`. Accent green `#2f6f5e` (sage), AccentBlue `#2563eb` for primary actions. `HoverBgBrush` is `#f8fafc` (cool). So the palette is *warm* (backgrounds) + *cool* (hover/divider) + *green* (accent) + *blue* (primary). That's three different temperature systems.

**What top apps do.**
- **Claude.ai** (legacy 2024): warm cream `#faf9f5`, accent orange. Pure 2-temperature.
- **ChatGPT** (post-2024): cool grays `#0d0d0d`/`#ececec`, accent pure black, no chroma. Pure 1-temperature.
- **Linear**: cool grays only, accent purple `#5e6ad2`. Pure 1-temperature.
- **Perplexity** (2025): cool white `#ffffff`, accent teal `#20808d`. Pure 1-temperature.

**Recommendation.**

1. **Pick a temperature direction.** The warm cream `#fafaf7` is on-brand for AIChat, keep it. But the cool `HoverBgBrush` and `DividerBrush` break the system. Change in `Tokens.axaml:36` from `#f8fafc` to `#f3efe6` (warm) and line 35 from `#e2e8f0` to `#e9e3d6` (already used by LineBrush, so just dedupe). Then add an `Alias` if Avalonia supports it, or just stop using `HoverBgBrush` in non-warm contexts.
2. **Reduce Bg vs Bg2 contrast.** Currently Bg `#fafaf7` vs Bg2 `#f1ede4` is ~6% darker — too much for a "subtle surface tone" use. Linear/Claude use ~1.5%. Change Bg2 in `Tokens.axaml:16` from `#f1ede4` to `#f6f2e9`. The sidebar rail will then sit ~3% off the main canvas, like Claude.
3. **Make the line/border the canonical 1px separator.** Bump `LineSoftBrush` usage everywhere instead of `LineBrush`. Currently `LineBrush` `#e9e3d6` (warm beige) is used by kbd-pill (`App.axaml:301`) which is fine. The default border color for surfaces should be `LineSoftBrush` `#f0ead9` (currently used by `input-floating` and `bubble-ai` — good).
4. **Drop `AccentBlue` from the sidebar new-chat button.** `MainWindow.axaml:105` uses `Classes="primary"` (blue) for "新对话". In every chat app I checked, the new-chat button is **neutral / dark**, not accent. ChatGPT uses near-black, Claude uses dark surface, Linear uses bg2. Change in `App.axaml:233–239`:
   - Replace `Background="{StaticResource AccentBlueBrush}"` with `Background="{StaticResource Bg2Brush}"`.
   - Change `Foreground="{StaticResource TextOnAccentBrush}"` to `Foreground="{StaticResource TextBrush}"`.
   - Change the `:pointerover` style to use `HoverBg2Brush`. Add the new style block:
   ```xml
   <Style Selector="Button.primary:pointerover">
       <Setter Property="Background" Value="{StaticResource HoverBg2Brush}" />
   </Style>
   ```
   This keeps the "primary" class semantic but makes the new-chat button a quiet surface — which is what Claude/Perplexity do.
5. **Reserve `AccentBlue` for one thing only**: the send button when `IsEnabled`. Right now the send button uses `AccentBrush` (green) which is fine, but the blue should win for *user action affordance*. See §5.
6. **Dark mode: keep `Bg #0d0d10`** but raise the text `Text2Color` contrast in `Tokens.Dark.axaml:32` from `#c8c8cf` to `#d8d8df` so muted-but-not-muted text reads as actual text, not background.

---

## 4. Conversation Bubbles

**What we have now.** AI bubbles: 28x28 avatar + 12px-radius surface card with `LineSoftBrush` border (`MainWindow.axaml:229–291`). User bubbles: right-aligned, `AccentSoftBrush` background, no border, MaxWidth 640 (`MainWindow.axaml:295–305`). System messages: centered, no bubble (`MainWindow.axaml:308–314`). Thinking state: 3 pulsing dots inside the AI bubble (`MainWindow.axaml:246–288`). No code-block header.

**What top apps do.**
- **Claude.ai**: no bubbles. AI text is left-aligned on a transparent background, with a small sparkle icon to the left. User text right-aligned with a subtle gray surface. This is the gold standard for "feels like a document, not a chat".
- **ChatGPT**: subtle right-aligned user surface, AI left-aligned transparent, both with no border. The 16px text does the heavy lifting.
- **Cursor**: AI has a 2px accent-colored left border on the bubble, code blocks have a header bar with filename + language + copy.

**Recommendation.**

1. **Add a code-block header bar.** Current MarkdownTextBlock at `MainWindow.axaml:242` and `:301` renders code with no header — the language label is buried inside the markdown. Add a wrapper style. Insert a new style in `App.axaml` after the bubble styles (around line 122):
   ```xml
   <Style Selector="Border.code-block">
       <Setter Property="Background" Value="{StaticResource Bg2Brush}" />
       <Setter Property="BorderBrush" Value="{StaticResource LineSoftBrush}" />
       <Setter Property="BorderThickness" Value="1" />
       <Setter Property="CornerRadius" Value="10" />
       <Setter Property="Margin" Value="0,8,0,0" />
   </Style>
   <Style Selector="Border.code-header">
       <Setter Property="Background" Value="{StaticResource HoverBg2Brush}" />
       <Setter Property="CornerRadius" Value="10,10,0,0" />
       <Setter Property="Padding" Value="10,6" />
       <Setter Property="BorderBrush" Value="{StaticResource LineSoftBrush}" />
       <Setter Property="BorderThickness" Value="0,0,0,1" />
   </Style>
   <Style Selector="TextBlock.code-lang">
       <Setter Property="FontFamily" Value="{StaticResource FontMono}" />
       <Setter Property="FontSize" Value="{StaticResource FontXs}" />
       <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
       <Setter Property="FontWeight" Value="Medium" />
   </Style>
   ```
   Implementation note: this requires the markdown renderer to emit a `code-block` + `code-header` Border. If `controls:MarkdownTextBlock` doesn't expose this, file it as a *non-blocking* follow-up and ship the rest of the changes. The bubble visual itself is independent.
2. **AI bubble: drop the `SurfaceBrush` background, keep the border.** Currently `App.axaml:117` sets `Background="{StaticResource SurfaceBrush}"`. Claude's no-bubble approach reads better; for AIChat's brand, a *borderless* AI surface is closer to the right answer. Edit `App.axaml:117`:
   ```xml
   <Setter Property="Background" Value="Transparent" />
   <Setter Property="BorderBrush" Value="{StaticResource LineSoftBrush}" />
   <Setter Property="BorderThickness" Value="0,0,0,1" />  <!-- subtle bottom rule only -->
   ```
   This gives AI messages a "document section" feel — divider under each AI response. Cursor does something similar.
3. **Symmetric conversation max-width.** `MainWindow.axaml:226` has `MaxWidth="720"` for AI, `:297` has `MaxWidth="640"` for user. The 80px asymmetry is invisible to users but makes a difference at narrow widths. Change both to `MaxWidth="720"`. ChatGPT, Claude, and Perplexity all use a single conversation width.
4. **User bubble: tighten the right padding.** `App.axaml:114` sets `Padding="12,9"`. Change to `"14,10"` — Claude's user surface is 14px horizontal, 10px vertical. Subtle but premium.
5. **AI avatar: change "AI" text → sparkle icon.** `MainWindow.axaml:230–233` shows the literal text "AI" in the 28x28 avatar. Claude uses ✦, Cursor uses a small dot grid, Perplexity uses its logo. Replace the `TextBlock Text="AI"` with:
   ```xml
   <Path Data="M12 2 L13.5 9.5 L21 11 L13.5 12.5 L12 20 L10.5 12.5 L3 11 L10.5 9.5 Z"
         Fill="{StaticResource AccentBrush}"
         Width="14" Height="14" Stretch="Uniform"
         HorizontalAlignment="Center" VerticalAlignment="Center"/>
   ```
6. **Thinking indicator: 3 dots → 1 dot bouncing.** The 3-pulse approach is fine but feels old. Cursor/ChatGPT use a single moving dot. Edit `MainWindow.axaml:246–288` to replace the three Ellipses with one `Ellipse` and an `Animation` that translates it 6px horizontally with `Duration="0:0:0.9"`. This is a 50-line change but high-visibility. If scope is tight, leave the 3-dot version and ship.

---

## 5. Input Composer

**What we have now.** `MainWindow.axaml:425–468` defines a 14px-radius composer with 1px border, 100px min-height, `FloatingShadow`, a model chip (pill) + `ToggleSwitch` for "只读" + send button. The send button is 36x36, 10px-radius, green. The composer is centered with `MaxWidth="860"`.

**What top apps do.**
- **ChatGPT**: composer has *no border*, just a soft shadow. Sits at the bottom with a max-width of ~768px. Send button morphs from arrow → stop-square while running. Has + (attach), tools, model selector, voice mic, send — all in one row.
- **Claude.ai**: similar. Composer has a hairline border only on the bottom (no rounded corners visible at the top), 1.2 rows default height expanding up to 8 rows.
- **Cursor**: composer is split — top half is the text area, bottom half is a toolbar with model + mode + send. Same general layout as AIChat.

**Recommendation.**

1. **Drop the 1px border, keep the shadow.** Edit `App.axaml:98`:
   ```xml
   <Setter Property="BorderBrush" Value="{StaticResource LineSoftBrush}" />
   <Setter Property="BorderThickness" Value="0" />
   ```
   (Currently line 99 has `BorderThickness="1"`.) This makes the composer feel like a card, not a form. ChatGPT, Notion, Linear all do this.
2. **Narrow the composer max-width from 860 → 760** at `MainWindow.axaml:425`. ChatGPT uses 768, Claude uses 720. 760 is the right number for the AIChat content area. The conversation max-width is 720 (§4.3) — the composer should be slightly wider than the content, not narrower.
3. **Replace the `ToggleSwitch` for "只读" with an icon button.** `MainWindow.axaml:451` — `ToggleSwitch` is a heavy control. Cursor uses a 28x28 icon button. Replace with:
   ```xml
   <Button Classes="icon-toggle" Command="{Binding ToggleNoWriteModeCommand}" 
           IsChecked="{Binding NoWriteMode}" ToolTip.Tip="只读模式 (⌘⇧R)">
     <Path Data="M2 12 a10 10 0 1 0 20 0 a10 10 0 1 0 -20 0 M12 8 v4 M12 16 v.01"
           Stroke="{Binding $parent[Button].Foreground}" StrokeThickness="1.5" 
           StrokeLineCap="Round" Width="14" Height="14" Stretch="Uniform"/>
   </Button>
   ```
   Add the style to `App.axaml`:
   ```xml
   <Style Selector="Button.icon-toggle">
       <Setter Property="Width" Value="28" /><Setter Property="Height" Value="28" />
       <Setter Property="Background" Value="Transparent" />
       <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
       <Setter Property="CornerRadius" Value="{StaticResource RadiusSm}" />
       <Setter Property="Padding" Value="0" /><Setter Property="BorderThickness" Value="0" />
   </Style>
   <Style Selector="Button.icon-toggle:pointerover">
       <Setter Property="Background" Value="{StaticResource HoverBgBrush}" />
   </Style>
   <Style Selector="Button.icon-toggle:checked">
       <Setter Property="Background" Value="{StaticResource AccentSoftBrush}" />
       <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
   </Style>
   ```
4. **Send button: morph to stop-square when running.** `MainWindow.axaml:455–464`. The `IsEnabled="{Binding !IsRunning}"` means while running the button is disabled, but `App.axaml:142–146` makes a disabled send button *look* like a "send" button still — just muted. ChatGPT replaces the icon with a square and the action with stop. Add a second `Path`:
   ```xml
   <Button ...>
       <Panel>
         <Path Data="M5 12 L19 12 M12 5 L19 12 L12 19" ... IsVisible="{Binding !IsRunning}" />
         <Path Data="M5 5 H19 V19 H5 Z" Fill="{Binding $parent[Button].Foreground}" 
               IsVisible="{Binding IsRunning}" Width="12" Height="12" 
               HorizontalAlignment="Center" VerticalAlignment="Center" />
       </Panel>
   </Button>
   ```
   Change the `Command` to a `SendOrStopCommand` that resolves to stop when running. This is a one-line VM change if the project already has `SendTaskCommand` and `StopTaskCommand` — if not, add a `StopTaskCommand` and a composite. Trivial.
5. **Add a ⌘↵ hint inside the send button or as a tiny keycap beside it.** Currently the tooltip mentions ⌘↵. Add an inline `TextBlock` after the send button at `MainWindow.axaml:464`:
   ```xml
   <Border Classes="kbd-pill" Margin="0,0,8,0" 
           IsVisible="{Binding !IsRunning}" VerticalAlignment="Center">
       <TextBlock Text="⌘ ⏎" Classes="kbd-inline" />
   </Border>
   ```
6. **Model chip: 999 radius → 8px radius.** `MainWindow.axaml:443` has `CornerRadius="999"` (pill). Claude's model chip is a small rect, not a pill. Change to `CornerRadius="8"` and `Padding="8,5"`. This reads as "chip" not "tag".
7. **Add a `+` attach button on the left edge of the bottom toolbar.** Cursor, ChatGPT, v0 all have this. Insert at the start of the `Grid` at `MainWindow.axaml:440`:
   ```xml
   <Button Grid.Column="0" Classes="icon-toggle" ToolTip.Tip="附加文件 (⌘⇧A)"
           Margin="0,0,4,0">
     <Path Data="M12 5 V19 M5 12 H19" 
           Stroke="{Binding $parent[Button].Foreground}" StrokeThickness="1.6" 
           StrokeLineCap="Round" Width="14" Height="14" Stretch="Uniform"/>
   </Button>
   ```
   And shift the model chip to `Grid.Column="1"`.

---

## 6. Status & Feedback

**What we have now.** The 32px status bar at `MainWindow.axaml:169–177` (sidebar bottom) shows `{StatusBarModel}` and a "设置" link. No context-meter, no progress indicator, no toast UI in the read sample.

**What top apps do.**
- **Cursor**: status bar shows current model + 4% context used as a 4px-tall progress strip.
- **ChatGPT**: status bar shows "ChatGPT can make mistakes" disclaimer + version.
- **Linear**: a 28px status bar with workspace name + keyboard hint.

**Recommendation.**

1. **Replace the sidebar-bottom status row with a proper status bar at the bottom of the window.** The 3-row grid at `MainWindow.axaml:40` already has `32` as row 3. Use it. Insert after the input composer grid (currently row 2) — or move the input composer into row 1 and dedicate row 2 to a proper status bar. Simpler: keep the existing layout, but remove the sidebar status row at `:169–177` and instead show that data in the bottom-of-window row.
2. **Add a context usage indicator** to the status bar. Show "context: 4% (1.2k / 32k)" with a thin 3px-tall progress bar. Cursor pattern. Insert a new `Style Selector="ProgressBar.context"` in `App.axaml`:
   ```xml
   <Style Selector="ProgressBar.context">
       <Setter Property="Height" Value="3" />
       <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
       <Setter Property="Background" Value="{StaticResource LineSoftBrush}" />
       <Setter Property="CornerRadius" Value="2" />
   </Style>
   ```
3. **Define a toast UI**. Only the `ToastShadow` brush and `ScrimBrush` exist; no actual toast surface. This is a 50–100 line addition; if scope is tight, file as follow-up.
4. **Streaming indicator on the AI bubble.** During streaming, ChatGPT shows a cursor blink at the end of the last chunk. Add a 2px-wide `Rectangle` at the end of the `MarkdownTextBlock` in the AI bubble (`MainWindow.axaml:289`) that is visible only when `IsRunning`:
   ```xml
   <Rectangle Width="2" Height="16" Fill="{StaticResource AccentBrush}" 
              IsVisible="{Binding IsRunning}" VerticalAlignment="Bottom" Margin="4,0,0,0">
     <Rectangle.Styles>
       <Style Selector="Rectangle">
         <Style.Animations>
           <Animation Duration="0:0:1" IterationCount="Infinite">
             <KeyFrame Cue="50%"><Setter Property="Opacity" Value="0.2"/></KeyFrame>
           </Animation>
         </Style.Animations>
       </Style>
     </Rectangle.Styles>
   </Rectangle>
   ```

---

## 7. Empty States & Onboarding

**What we have now.** `MainWindow.axaml:323–420` shows a centered vertical stack: hero title "Ask AIChat anything" (32px), 460px sub-greeting, 2x2 grid of 280x92 quick-action cards, then a "提示 ⌘K 命令面板 ⌘↵ 发送" row. Total visual mass is high.

**What top apps do.**
- **Claude.ai**: empty state = big "How can I help you today?" + a single suggested prompt example. No grid.
- **ChatGPT**: empty state = "What can I help with?" + a few example chips (text-only, no card).
- **v0.dev**: hero prompt input already on the page; below it 4 cards with icon + label, no description.
- **Notion AI**: hero = AI button in a toolbar. No dedicated empty state.

**Recommendation.**

1. **Drop the sub-greeting and the kbd-hint row.** `MainWindow.axaml:332–339` (sub-greeting) and `:409–419` (kbd row) — both are noise. Keep only the hero title + 4 cards. After deletion, the empty state becomes: hero (28px) + 16px gap + 4 cards in a 2x2 grid. That's the v0 pattern.
2. **Unify the language.** "Ask AIChat anything" mixes English + brand. Either fully Chinese (用什么我可以帮你？) or fully English. Pick one and use it everywhere.
3. **Card height: 92 → 80, drop the description text on cards.** v0's cards are 64–72px tall with just icon + title. Edit `MainWindow.axaml:347, 362, 377, 392` (each quick-action Button): change `Height="92"` to `Height="76"` and delete the `TextBlock` description at lines 359, 374, 389, 404 (the `<TextBlock Text="跑测试…" Classes="muted" … />` ones).
4. **Use a `ToolTip.Tip` on each card to show the full prompt text on hover.** This keeps the cards lean while preserving discoverability. The `Tag` attribute is already there (line 347 etc) — promote it to a tooltip:
   ```xml
   <Button ... Tag="检查发布前风险…" ToolTip.Tip="检查发布前风险并修复失败的测试"
           Click="EmptyStateCard_OnClick">
   ```
5. **Hero title: align to a typography token.** `App.axaml:52` already binds to `FontDisplay` (§2.1). Make sure `TextBlock` at `MainWindow.axaml:330` uses `Classes="hero-title"`, which it does — good.

---

## 8. Animations & Micro-interactions

**What we have now.** Three animation blocks: the 3-dot thinking pulse (`MainWindow.axaml:253–288`), and that's it. No hover transitions, no focus rings, no bubble fade-in. The `:pointerover` styles just snap.

**What top apps do.**
- **Linear**: every hover, active, focus state has a 120ms ease-out transition.
- **ChatGPT**: bubbles fade in 200ms after streaming completes.
- **Raycast**: list items have 100ms color tween on hover.

**Recommendation.**

1. **Add a global `Transitions` default to all interactive elements.** In `App.axaml` near line 30, after the `Window` style, add:
   ```xml
   <Style Selector="Button">
       <Setter Property="Transitions">
           <Transitions>
               <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.12" />
               <BrushTransition Property="Background" Duration="0:0:0.12" />
               <BrushTransition Property="Foreground" Duration="0:0:0.12" />
           </Transitions>
       </Setter>
   </Style>
   <Style Selector="ListBoxItem">
       <Setter Property="Transitions">
           <Transitions>
               <BrushTransition Property="Background" Duration="0:0:0.12" />
           </Transitions>
       </Setter>
   </Style>
   ```
   This is the single highest-impact change for "feels premium" with one block of code.
2. **Add a focus ring style.** After line 33 (the Window style):
   ```xml
   <Style Selector="Button:focus /template/ ContentPresenter#PART_ContentPresenter">
       <Setter Property="Background" Value="{StaticResource HoverBgBrush}" />
   </Style>
   ```
   (The exact template name may differ for FluentTheme; if it doesn't bind, file as follow-up.) For text input, FluentTheme provides a focus underline by default — verify it's not lost when the `TextBox` style is applied (it should be).
3. **Add a fade-in for new conversation bubbles.** On `MainWindow.axaml:222` (the `Grid` in the DataTemplate), add a `Classes="bubble-enter"` and a style:
   ```xml
   <Style Selector="Grid.bubble-enter">
       <Style.Animations>
           <Animation Duration="0:0:0.18" Easing="CubicEaseOut">
               <KeyFrame Cue="0%"><Setter Property="Opacity" Value="0"/><Setter Property="TranslateTransform.Y" Value="6"/></KeyFrame>
               <KeyFrame Cue="100%"><Setter Property="Opacity" Value="1"/><Setter Property="TranslateTransform.Y" Value="0"/></KeyFrame>
           </Animation>
       </Style.Animations>
   </Style>
   ```
4. **Sidebar row hover: 100ms background tween.** Already covered by the global transition in #1.

---

## 9. Keyboard Shortcuts & Discoverability

**What we have now.** Tooltips on toolbar buttons reference ⌘K, ⌘N, ⌘,, ⌘⇧T, ⌘⇧R, ⌘↵. The empty state shows ⌘K and ⌘↵ in kbd-pills. A ⌘K palette exists somewhere past line 470 of `MainWindow.axaml`. No `?` help overlay.

**What top apps do.**
- **ChatGPT**: ⌘? opens a "Keyboard shortcuts" panel listing every shortcut.
- **Linear**: ⌘K is the command palette (search + commands), ⌘/ opens a smaller shortcut bar.
- **Raycast**: ⌘? is the help shortcut in the palette; every result row has a hint on the right.

**Recommendation.**

1. **Add a `?` help overlay** that lists all shortcuts. The current overlays (palette, settings modal) suggest a pattern at `MainWindow.axaml:470+`. Add a third modal triggered by `?` showing a 480x520 panel with a 2-column shortcut list. Use the same `ScrimBrush` and `ModalShadow` tokens already defined.
2. **Add a "Press ? for shortcuts" link to the status bar.** `MainWindow.axaml:169` area. Add a `TextBlock` with `Classes="link-button"` and `Text="快捷键 (Press ?)"`, `Command={Binding ShowShortcutsCommand}`.
3. **Show keyboard hints on the right side of sidebar rows.** This is the Raycast pattern. The `sidebar-row` at `MainWindow.axaml:171` doesn't currently show shortcut hints. If the conversation list later has per-conversation shortcuts (rename ⌘R, delete ⌘⌫), render a small `TextBlock Classes="kbd-inline"` on the right of each row visible only on hover.
4. **Add a ⌘⇧N shortcut for "new project"** alongside ⌘N for "new conversation". Right now there's a project button in the sidebar that has no shortcut.
5. **Composer placeholder text should hint shortcuts.** `MainWindow.axaml:430` has `PlaceholderText="Ask anything…"`. Change to:
   ```xml
   PlaceholderText="Ask anything…  (按 ⌘↵ 发送)"
   ```
   The `Ask anything` text is correct but lacks the action hint.

---

## 10. Component Polish

**What we have now.** Token-driven spacing, but several inline values still leak into XAML: `Padding="10,5"`, `Padding="14,12"`, `Margin="0,0,10,10"`, `FontSize="11"`, `CornerRadius="14"`, `Width="14" Height="14"`, `Margin="32,0,0,0"`.

**What top apps do.**
- **Linear**: every padding, margin, radius, font-size, color is a token. Inline values exist only in token definitions.
- **ChatGPT**: 1–2% deviation from token. The few inline values are recent additions.

**Recommendation.**

1. **Audit and replace inline values with tokens** in `MainWindow.axaml`:
   - `:143` `Padding="14,12"` → `Padding="{StaticResource Space3}"` (already 12) — actually `Space3=12`, `Space4=16` — so `14` is hand-rolled. **Add a `Space5` token to `Tokens.axaml`: `<Thickness x:Key="Space5">14</Thickness>`** and `<x:Double x:Key="SpaceGap5">14</x:Double>`. Then replace all `14` paddings/margins with `StaticResource Space5`/`StaticResource SpaceGap5`.
   - `:347, 362, 377, 392` `Margin="0,0,10,10"` → `Margin="0,0,{StaticResource Space3},{StaticResource Space3}"`.
   - `:359, 374, 389, 404` `Margin="32,0,0,0"` → `Margin="{StaticResource SpaceGap6},0,0,0"`.
   - `:412, 416` `FontSize="10"` → `FontSize="{StaticResource Font2xs}"`.
   - `:443` `CornerRadius="999"` and `Padding="10,5"` → use a new `Style Selector="Border.model-chip"` (see below).
2. **Add a `model-chip` style** to `App.axaml` after the badge styles (~line 202):
   ```xml
   <Style Selector="Border.model-chip">
       <Setter Property="Background" Value="{StaticResource Bg2Brush}" />
       <Setter Property="CornerRadius" Value="{StaticResource RadiusSm}" />
       <Setter Property="Padding" Value="8,5" />
   </Style>
   ```
   Then `MainWindow.axaml:443` collapses from 7 lines of inline `Background`, `CornerRadius`, `Padding` to `<Border Classes="model-chip" …>`.
3. **Add an `empty-state-card` height token** to `App.axaml:275` (currently uses inline `Width="280" Height="92"` in 4 places at `:347, 362, 377, 392`). Move to a style:
   ```xml
   <Style Selector="Button.empty-state-card">
       <Setter Property="Width" Value="280" />
       <Setter Property="Height" Value="76" />  <!-- was 92; see §7.3 -->
       <Setter Property="Background" Value="{StaticResource SurfaceBrush}" />
       <Setter Property="BorderBrush" Value="{StaticResource LineSoftBrush}" />
       <Setter Property="BorderThickness" Value="1" />
       <Setter Property="CornerRadius" Value="{StaticResource RadiusLg}" />
       <Setter Property="Padding" Value="14,12" />
       <Setter Property="HorizontalContentAlignment" Value="Stretch" />
   </Style>
   ```
   The current `App.axaml:275` has `Margin="0,0,8,8"` and `Padding="14,12"` — both are already token-friendly. Good.
4. **Sidebar-row selected state should also show a leading dot.** Linear pattern. Add a 6x6 `Ellipse` at the left edge of the `.selected` variant, visible only when the row is selected. This requires editing the `DataTemplate` at `MainWindow.axaml:154–162` to add:
   ```xml
   <Grid ColumnDefinitions="Auto,*" ...>
       <Ellipse Grid.Column="0" Width="6" Height="6" Fill="{StaticResource AccentBrush}"
                VerticalAlignment="Center" Margin="0,0,8,0"
                IsVisible="{Binding IsSelected}" />
       <StackPanel Grid.Column="1" Spacing="2">...
   ```
5. **Reduce the input-floating padding from 14,12 to 12,10.** `App.axaml:101` — Claude/ChatGPT use 12-14px horizontal, 10px vertical. Tightening this makes the composer feel less "wrapped", more native.

---

## Priority order — top 10 most impactful changes

These are ordered by **user-visible impact × effort**. Ship order.

| # | Change | Where | Impact | Effort |
|---|--------|-------|--------|--------|
| 1 | **Remove the always-visible hero header from the main column** — let the conversation start at the top. | `MainWindow.axaml:187–211` (delete); keep the project breadcrumb only as a 36px row. | High — biggest IA change. Removes 80px of wasted space and matches Claude/ChatGPT/Perplexity. | 30 min |
| 2 | **Add a global 120ms transition block** to `Button` and `ListBoxItem`. | New `<Style Selector="Button">` in `App.axaml:~30` | High — every interaction feels more premium with one block of code. | 10 min |
| 3 | **Drop the input-floating border, keep the shadow.** | `App.axaml:99` `BorderThickness="1"` → `0`. | High — composer stops looking like a form, starts looking like a chat surface. | 5 min |
| 4 | **Replace hero `FontSize="32"` with the new `FontDisplay` token, expand the font-size ramp, add letter-spacing tokens.** | `Tokens.axaml:98–103` + `App.axaml:52`. | High — typography consistency across the whole app. | 30 min |
| 5 | **De-warm the chrome**: change `HoverBgBrush` `#f8fafc` → `#f3efe6`, dedupe divider color. | `Tokens.axaml:35–36`. | Medium — fixes the warm-cool palette mix. | 10 min |
| 6 | **New chat button: blue → neutral surface (Bg2 + dark text).** | `App.axaml:233–239`. | Medium — new-chat button no longer screams for attention. Matches Claude/Perplexity. | 15 min |
| 7 | **Replace `ToggleSwitch` "只读" with a 28x28 icon button; add a `+` attach button and a stop-morphing send button.** | `MainWindow.axaml:425–468` + new `icon-toggle` style. | High — composer is the *primary surface*; making it 4-icon-wide (attach / model / readonly / send-or-stop) is the modern pattern. | 1 hr |
| 8 | **Sidebar 260 → 264, add a search input, add the selected-row leading dot.** | `MainWindow.axaml:99, 103` + new DataTemplate. | Medium — small but the search input is a high-frequency feature. | 30 min |
| 9 | **Add `Space5=14` and `Font2xs=10` and `FontLg=15` tokens; replace inline paddings/margins/font-sizes throughout `MainWindow.axaml`.** | `Tokens.axaml:77–86, 98–103` + ~20 inline replacements in `MainWindow.axaml`. | Medium — design-system hygiene. Prevents the "every-new-screen-is-hand-tuned" drift. | 1 hr |
| 10 | **Empty state: drop the sub-greeting and the kbd-hint row; cards drop to 76px and drop the description line; use `ToolTip.Tip` for the full prompt.** | `MainWindow.axaml:323–420`. | High — first impression. v0/Claude pattern: lean and intentional. | 30 min |

**Total estimated effort: 4–5 hours for items 1–10.** Items 4, 5, 6, 8, 9 are pure token + style-block changes; items 1, 2, 3, 7, 10 are XAML structural changes.

**Defer (not blocking, ship in v1.1):**
- Toast UI (only the brushes exist today).
- Code-block header bar in markdown (depends on the markdown renderer's API).
- Single bouncing dot for the thinking indicator.
- ⌘? help overlay.
- Status bar with context usage.
- Streaming cursor at the end of the AI bubble.
