using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace media_management_app.Views;

public partial class AddMediaLockDialog : Window, INotifyPropertyChanged
{
    private static readonly string[] RequiredPhrases =
    [
        "May Co Chac Khong",
        "Can Than Day!"
    ];

    private int _phraseIndex;
    private string _typedText = string.Empty;

    public AddMediaLockDialog(string mediaTitle)
    {
        InitializeComponent();
        MediaTitle = string.IsNullOrWhiteSpace(mediaTitle) ? "this title" : mediaTitle.Trim();
        DataContext = this;
        System.Windows.DataObject.AddPastingHandler(PhraseBox, OnPasting);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MediaTitle { get; }

    public string StepLabel => $"{_phraseIndex + 1} / {RequiredPhrases.Length}";

    public string CurrentPhrase => RequiredPhrases[_phraseIndex];

    public string ConfirmButtonText => _phraseIndex == RequiredPhrases.Length - 1 ? "Unlock" : "Next";

    public bool CanConfirm => string.Equals(TypedText, CurrentPhrase, StringComparison.Ordinal);

    public string TypedText
    {
        get => _typedText;
        set
        {
            if (_typedText == value)
            {
                return;
            }

            _typedText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PhraseBox.Focus();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanConfirm)
        {
            Confirm_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfirm)
        {
            return;
        }

        if (_phraseIndex < RequiredPhrases.Length - 1)
        {
            _phraseIndex++;
            TypedText = string.Empty;
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(CurrentPhrase));
            OnPropertyChanged(nameof(ConfirmButtonText));
            OnPropertyChanged(nameof(CanConfirm));
            PhraseBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PhraseBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key is Key.C or Key.V or Key.X or Key.A or Key.Insert)
        {
            e.Handled = true;
            return;
        }

        if (shift && e.Key == Key.Insert)
        {
            e.Handled = true;
        }
    }

    private void PhraseBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PhraseBox_PreviewDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
    }

    private void OnClipboardCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = false;
        e.Handled = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
