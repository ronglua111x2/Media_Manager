using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class CartCandidatePickerControl : System.Windows.Controls.UserControl
{
    private System.Collections.Specialized.INotifyCollectionChanged? _observedCandidates;

    public CartCandidatePickerControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            ClosePopup();
            ObserveCandidates();
        };
        Loaded += (_, _) => ObserveCandidates();
        Unloaded += (_, _) => StopObservingCandidates();
        CandidatePopup.CustomPopupPlacementCallback = OnPlacePopup;
        // Before the toggle opens the popup (mouse), and when it does (keyboard): size it for the room below.
        PickerToggle.PreviewMouseLeftButtonDown += (_, _) => FitPopupBelow();
        PickerToggle.Checked += (_, _) => FitPopupBelow();
    }

    /// <summary>Tallest the candidate list gets; less when the screen has less room below the picker.</summary>
    private const double PreferredPopupHeight = 460;

    /// <summary>Below this the list is unusable; the popup then keeps this height even if WPF has to nudge it.</summary>
    private const double MinPopupHeight = 200;

    /// <summary>
    /// Always below the picker, left edges aligned (the card's margin, room for its shadow, is shifted out). One
    /// placement only: WPF's Bottom placement flips the popup above the picker when it does not fit below, which
    /// <see cref="FitPopupBelow"/> prevents by capping the height.
    /// </summary>
    private System.Windows.Controls.Primitives.CustomPopupPlacement[] OnPlacePopup(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        System.Windows.Point offset)
    {
        // The callback works in the popup's own units (device pixels on a scaled display): scale the DIP margin.
        var scale = PickerToggle.ActualWidth > 0 ? targetSize.Width / PickerToggle.ActualWidth : 1;
        return
        [
            new System.Windows.Controls.Primitives.CustomPopupPlacement(
                new System.Windows.Point(-PopupCard.Margin.Left * scale, targetSize.Height),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
        ];
    }

    /// <summary>Caps the popup's height to the work area below the picker, so it opens downwards and scrolls.</summary>
    private void FitPopupBelow()
    {
        if (System.Windows.PresentationSource.FromVisual(PickerToggle) is not { CompositionTarget: { } target })
        {
            return;
        }

        var bottomLeft = PickerToggle.PointToScreen(new System.Windows.Point(0, PickerToggle.ActualHeight));
        var workArea = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)bottomLeft.X, (int)bottomLeft.Y)).WorkingArea;
        var roomBelow = target.TransformFromDevice.Transform(new System.Windows.Vector(0, workArea.Bottom - bottomLeft.Y)).Y;
        var height = roomBelow - PopupCard.Margin.Top - PopupCard.Margin.Bottom;
        PopupCard.MaxHeight = Math.Clamp(height, MinPopupHeight, PreferredPopupHeight);
    }

    /// <summary>
    /// When the order's candidates are reloaded (re-search, blacklist), the quick filter is cleared first. Otherwise the
    /// reloaded selection can be filtered out of the list, and the two-way SelectedValue binding then clears the
    /// order's selected candidate (07c NS-30).
    /// </summary>
    private void ObserveCandidates()
    {
        StopObservingCandidates();
        _observedCandidates = (DataContext as TorrentOrderViewModel)?.Candidates;
        if (_observedCandidates is not null)
        {
            _observedCandidates.CollectionChanged += OnCandidatesChanged;
        }
    }

    private void StopObservingCandidates()
    {
        if (_observedCandidates is not null)
        {
            _observedCandidates.CollectionChanged -= OnCandidatesChanged;
            _observedCandidates = null;
        }
    }

    private void OnCandidatesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                or System.Collections.Specialized.NotifyCollectionChangedAction.Remove
                or System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
            CandidateFilterBox is { Text.Length: > 0 })
        {
            CandidateFilterBox.Text = string.Empty;
        }
    }

    private void CandidateListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not TorrentOrderViewModel ||
            sender is not System.Windows.Controls.ListBox ||
            e.AddedItems.Count == 0)
        {
            return;
        }

        ClosePopup();
    }

    /// <summary>
    /// Quick filter over many encodes (native-search P5): every typed word must appear in the candidate's
    /// name or attributes. The order's selected candidate always stays visible, so filtering never
    /// changes the selection.
    /// </summary>
    private void CandidateFilterBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CandidateListBox.ItemsSource);
        if (view is null)
        {
            return;
        }

        var words = CandidateFilterBox.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        view.Filter = words.Length == 0
            ? null
            : item => item is TorrentOrderCandidateViewModel candidate &&
                      ((DataContext is TorrentOrderViewModel { SelectedCandidateId: { } selectedId } && candidate.Id == selectedId) ||
                       ReferenceEquals(candidate, CandidateListBox.SelectedItem) ||
                       words.All(word => candidate.FilterText.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private void BlacklistButton_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Command: { } command } button &&
            command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    private void ClosePopup()
    {
        PickerToggle.IsChecked = false;
        if (CandidateFilterBox is not null && CandidateFilterBox.Text.Length > 0)
        {
            CandidateFilterBox.Text = string.Empty;
        }
    }
}
