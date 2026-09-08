# Layout stability

Avoid UI that balloons, reflows, or jumps when content or override state changes.

## Fixed rows over Auto

When a row can swap summary text for an editor, give it a **fixed height** (cart overview uses 34px). Do not let Auto height grow when a NumericUpDown or TextBox appears.

## Overlay, do not collapse

Stack the recipe value and the editor in the same cell (`CartOverrideValueHostStyle`). Toggle with `Hidden` / `Visible`, not `Collapsed`. Collapsed removes layout space and makes the row jump.

## Long text

- `TextWrapping="NoWrap"`
- `TextTrimming="CharacterEllipsis"`
- `ToolTip` with the full string

Wrapping is what made Episode vs Pack “Titles” rows misalign.

## Width

- ComboBox: `MinWidth="0"` and `HorizontalAlignment="Stretch"` so paired Episode/Pack columns stay even.
- NumericUpDown: set Width and Height explicitly. Steppers clip around 78px; keep about 112–128px for two-digit values.

## Nested scroll

Do not grow the parent to fit every row. Give inner overview cards a **fixed viewport** (`CartRecipeOverviewScrollStyle` Height 238 = 7×34px) and scroll the rest.

Paired Episode/Pack cards: [`Views/SyncedScrollViewer.cs`](../../Views/SyncedScrollViewer.cs) with the same `GroupName` so both viewports move together. Auto-Track show cards bind a unique `OverviewScrollGroupName` (`AutoTrackRecipe-{ShowId}`) so they do not sync with Cart or each other.

Avoid a `*` row inside an Auto-height parent; it can swallow the scrollbar or expand without limit.

## Enter on text fields

Single-line override TextBoxes should commit on Enter as well as LostFocus. Use `views:CommitTextBoxOnEnter.IsEnabled="True"` ([`Views/CommitTextBoxOnEnter.cs`](../../Views/CommitTextBoxOnEnter.cs)).

## Focus after click

WPF leaves `IsKeyboardFocused` on a Button after a mouse click, so the accent border can stay until something else is focused. Accept that default. Do not `Keyboard.ClearFocus()` after click — it breaks commands. Tooltip-only `?` icons stay non-focusable `Border` hosts, not Buttons.
