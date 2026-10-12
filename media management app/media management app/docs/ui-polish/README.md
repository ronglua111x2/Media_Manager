# UI polish

Guides for adding or changing WPF UI without breaking the dark theme, layout, or bindings.

| File | When to read |
|------|----------------|
| [THEME.md](./THEME.md) | Colors, accents, which `AppBrush*` to use |
| [LAYOUT.md](./LAYOUT.md) | Fixed sizes, overlays, no ballooning or jumping |
| [BINDINGS.md](./BINDINGS.md) | `OneWay` vs `TwoWay`; `Run.Text` crashes |
| [LUCIDE_ICONS.md](./LUCIDE_ICONS.md) | Valid `PackIconLucideKind` names (grep before setting `Kind`) |

Source tokens: `Resources/AppThemeColors.Dark.xaml` (and the Light twin). Shared styles: `Resources/AppStyles.xaml`.
