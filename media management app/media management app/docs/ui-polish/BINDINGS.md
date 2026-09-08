# Bindings: OneWay vs TwoWay

WPF defaults some properties to **TwoWay**. Binding a **get-only** view-model property crashes on load or expand (cart recipe panel: `Run.Text` / display fields without a setter).

## Rules

- Read-only display: set `Mode=OneWay` on `Run.Text` and on `TextBlock.Text` for computed or effective properties.
- TwoWay only on **editable** fields (`OverrideMinSeeders`, `OverrideMinSizeGbText`, ComboBox `SelectedValue`, CheckBox `IsChecked`, and similar).
- If the property has no setter, never TwoWay.

Example in [`Resources/WorkspaceSharedTemplates.xaml`](../../Resources/WorkspaceSharedTemplates.xaml): recipe max candidates uses `Mode=OneWay` because `RecipeMaxCandidates` is get-only.

```xml
<TextBlock Text="{Binding RecipeMaxCandidates, Mode=OneWay}" />
```

`Run.Text` is TwoWay by default even when the `Run` sits inside a read-only `TextBlock`. Always specify OneWay for display-only runs (see Stats and Auto-Track views).

## Cart overrides

Toggle **enabled** with a command on the title. Bind editors to `Override*` properties that have setters. Do not bind TwoWay to `Effective*` or recipe summary strings.
