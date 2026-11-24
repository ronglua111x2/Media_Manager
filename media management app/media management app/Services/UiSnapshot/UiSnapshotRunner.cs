using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.ViewModels;
using media_management_app.Views;

namespace media_management_app.Services.UiSnapshot;

/// <summary>
/// Dev-only <c>--ui-snapshots</c> run: shows the main window at the 1280x720 design size, visits every
/// workspace and Settings section, opens the console and a few dialogs with placeholder data, writes one PNG
/// per screen plus <c>index.md</c>, then shuts the app down. Exit code 0 = all captured, 1 = some failed.
/// See docs/UI_SNAPSHOTS.md.
/// </summary>
public sealed partial class UiSnapshotRunner
{
    public const double DesignWidth = 1280;
    public const double DesignHeight = 720;
    /// <summary>
    /// Optional <c>{"commit": "...", "settleMs": 2500}</c> in the output folder, written by scripts/ui-snapshots.ps1.
    /// A file, not environment variables: the exe is requireAdministrator and starts through UAC.
    /// </summary>
    public const string RequestFileName = "snapshot-request.json";

    private static readonly TimeSpan BusyWaitLimit = TimeSpan.FromSeconds(15);

    private readonly MainWindow _window;
    private readonly MainViewModel _mainViewModel;
    private readonly IConsoleWindowService _consoleWindowService;
    private readonly ITorrentAddDiskAssignmentService _diskAssignmentService;
    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;
    private readonly string _outputFolder;
    private readonly int _settleMs = 2500;
    private readonly string? _commit;
    private readonly List<(string File, string Caption)> _captured = [];
    private readonly List<(string Name, string Error)> _failed = [];
    private int _sequence;

    public UiSnapshotRunner(
        MainWindow window,
        MainViewModel mainViewModel,
        IConsoleWindowService consoleWindowService,
        ITorrentAddDiskAssignmentService diskAssignmentService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
        IOperationProgressService progressService,
        IAppLogger logger,
        string outputFolder)
    {
        _window = window;
        _mainViewModel = mainViewModel;
        _consoleWindowService = consoleWindowService;
        _diskAssignmentService = diskAssignmentService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _progressService = progressService;
        _logger = logger;
        _outputFolder = outputFolder;
        var request = ReadRequest(Path.Combine(outputFolder, RequestFileName));
        _commit = request?.Commit;
        if (request?.SettleMs is >= 0 and <= 60_000)
        {
            _settleMs = request.SettleMs.Value;
        }
    }

    private sealed record SnapshotRequest(string? Commit, int? SettleMs);

    private static SnapshotRequest? ReadRequest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var request = System.Text.Json.JsonSerializer.Deserialize<SnapshotRequest>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            File.Delete(path);
            return request;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_outputFolder);
        _logger.Info($"UI snapshots: writing to '{_outputFolder}'.", LogTarget.All);

        PrepareMainWindow();
        await SettleAsync(_settleMs);

        await CaptureWorkspacesAsync();
        await CaptureSelectionVariantsAsync();
        await CaptureSettingsSectionsAsync();
        await CaptureExpandedSidebarAsync();
        await CaptureConsoleAsync();
        await CaptureDialogAsync("dialog-add-lock", "Add media: lock phrase dialog (Find/Add)", () => new AddMediaLockDialog("Show 01"));
        await CaptureDialogAsync("dialog-episode-organization", "Episode organization dialog (Find/Add, Library)", CreateEpisodeOrganizationDialog);
        await CaptureDialogAsync("dialog-disk-plan", "Add torrents: disk plan dialog (Cart/Torrent)", CreateDiskPlanDialog);
        await CaptureCandidateListAsync();

        WriteIndex();
        _logger.Info(
            $"UI snapshots: {_captured.Count} captured, {_failed.Count} failed. Folder '{_outputFolder}'.",
            LogTarget.All);
        return _failed.Count == 0 ? 0 : 1;
    }

    #region Screens

    private void PrepareMainWindow()
    {
        _window.WindowState = WindowState.Normal;
        _window.Width = DesignWidth;
        _window.Height = DesignHeight;
        _window.Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Left + ((SystemParameters.WorkArea.Width - DesignWidth) / 2));
        _window.Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Top + ((SystemParameters.WorkArea.Height - DesignHeight) / 2));
        _mainViewModel.IsSidebarCollapsed = true;
    }

    private async Task CaptureWorkspacesAsync()
    {
        foreach (var item in _mainViewModel.NavigationItems.Where(item => item.Kind != AppWorkspaceKind.SystemSettings).ToList())
        {
            await CaptureStepAsync(
                $"workspace-{Slug(item.Label)}",
                $"{item.Label} workspace ({item.Description})",
                async () =>
                {
                    _mainViewModel.NavigateCommand.Execute(item);
                    await SettleAsync(_settleMs);
                    return RenderWindow(_window);
                });
        }
    }

    /// <summary>
    /// The saved selection may be a movie with an empty cart; show detail and a filled cart carry most of
    /// the per-item buttons, so capture those too (selection is written to the sandbox settings only).
    /// </summary>
    private async Task CaptureSelectionVariantsAsync()
    {
        await CaptureStepAsync(
            "workspace-library-show-detail",
            "Library workspace with a show selected (season/episode actions)",
            async () =>
            {
                NavigateTo(AppWorkspaceKind.Library);
                await SettleAsync(Math.Min(_settleMs, 1200));
                var library = _mainViewModel.CurrentView as LibraryViewModel
                              ?? throw new InvalidOperationException("Library did not open.");
                library.SelectedMediaCard = library.MediaCards
                                                .Where(card => card.MediaKind != MediaKind.Movie)
                                                .OrderByDescending(card => card.OrderCount)
                                                .FirstOrDefault()
                                            ?? throw new InvalidOperationException("No show in the library.");
                await SettleAsync(_settleMs);
                return RenderWindow(_window);
            });

        await CaptureStepAsync(
            "workspace-torrent-cart-with-orders",
            "Torrent workspace with the fullest cart selected (order rows, candidate pickers)",
            async () =>
            {
                NavigateTo(AppWorkspaceKind.Torrent);
                await SettleAsync(Math.Min(_settleMs, 1200));
                var torrent = _mainViewModel.CurrentView as TorrentWorkspaceViewModel
                              ?? throw new InvalidOperationException("Torrent workspace did not open.");
                torrent.SelectedMediaCard = torrent.MediaCards
                                                .Where(card => card.OrderCount > 0)
                                                .OrderByDescending(card => card.OrderCount)
                                                .FirstOrDefault()
                                            ?? throw new InvalidOperationException("No cart has orders.");
                await SettleAsync(_settleMs);
                return RenderWindow(_window);
            });

        await CaptureCartRunningAsync();
    }

    /// <summary>
    /// "Cart is running" states, staged in memory on the selected cart (a real run would search indexers
    /// and talk to qBittorrent): Run Cart mid-search, and Add mid-way through adding to qBittorrent.
    /// Mirrors what the run sets: IsRunningCart, order statuses, the shell progress pill, the status line.
    /// Nothing is written to the database; the original rows are put back afterwards.
    /// </summary>
    private async Task CaptureCartRunningAsync()
    {
        if (_mainViewModel.CurrentView is not TorrentWorkspaceViewModel torrent || torrent.Orders.Count == 0)
        {
            _failed.Add(("cart-running", "No cart with orders is selected; skipped the cart-running captures."));
            return;
        }

        var original = torrent.Orders.ToList();
        var originalStatus = torrent.StatusMessage;
        // Rows 1-3 are what fits above the fold at 720 px: keep the "current" row among them.
        var searchingIndex = Math.Min(original.Count - 1, 2);
        var addingIndex = Math.Min(original.Count - 1, 1);
        try
        {
            await CaptureStepAsync(
                "workspace-torrent-cart-running-search",
                "Torrent workspace while Run Cart is searching (staged: earlier orders found candidates, one searching, rest waiting)",
                async () =>
                {
                    StageOrders(torrent, original, (order, index) => index < searchingIndex
                        ? (order.HasCandidates ? TorrentOrderStatus.CandidatesFound : TorrentOrderStatus.NoCandidates,
                           order.HasCandidates ? $"{order.Candidates.Count} candidate(s) found." : "No candidates matched the recipe.",
                           order.HasCandidates)
                        : index == searchingIndex
                            ? (TorrentOrderStatus.Searching, "Searching with recipe: (cart recipe)", false)
                            : (TorrentOrderStatus.Draft, string.Empty, false));
                    _progressService.Start("Search: preparing…", 8);
                    _progressService.Report(3, "Search: Query 3/8");
                    await SettleAsync(Math.Min(_settleMs, 1500));
                    return RenderWindow(_window);
                });

            await CaptureStepAsync(
                "workspace-torrent-cart-running-add",
                "Torrent workspace while Add is sending torrents to qBittorrent (staged)",
                async () =>
                {
                    _progressService.Finish("Idle");
                    StageOrders(torrent, original, (order, index) => index < addingIndex
                        ? (TorrentOrderStatus.AddedToClient, "Added to qBittorrent.", true)
                        : (TorrentOrderStatus.Approved, "Approved.", true));
                    var current = torrent.Orders[addingIndex];
                    torrent.StatusMessage = $"Adding {addingIndex + 1}/{torrent.Orders.Count}: {current.Title}...";
                    await SettleAsync(Math.Min(_settleMs, 1500));
                    return RenderWindow(_window);
                });
        }
        finally
        {
            _progressService.Finish("Idle");
            torrent.IsRunningCart = false;
            TorrentOrderViewModel.CartOperationRunning = false;
            torrent.Orders.Clear();
            foreach (var order in original)
            {
                torrent.Orders.Add(order);
            }

            torrent.StatusMessage = originalStatus;
        }
    }

    private static void StageOrders(
        TorrentWorkspaceViewModel torrent,
        IReadOnlyList<TorrentOrderViewModel> original,
        Func<TorrentOrderViewModel, int, (TorrentOrderStatus Status, string Detail, bool KeepCandidates)> stage)
    {
        torrent.IsRunningCart = true;
        TorrentOrderViewModel.CartOperationRunning = true;
        torrent.Orders.Clear();
        for (var i = 0; i < original.Count; i++)
        {
            var source = original[i];
            var (status, detail, keepCandidates) = stage(source, i);
            var copy = new TorrentOrderViewModel
            {
                Id = source.Id,
                MediaId = source.MediaId,
                TargetKind = source.TargetKind,
                SeasonNumber = source.SeasonNumber,
                EpisodeId = source.EpisodeId,
                Title = source.Title,
                Summary = source.Summary,
                Status = status,
                StatusDetail = detail,
                SelectedCandidateName = keepCandidates ? source.SelectedCandidateName : string.Empty,
                SelectedCandidateSeeders = keepCandidates ? source.SelectedCandidateSeeders : 0,
                SelectedCandidateQuality = keepCandidates ? source.SelectedCandidateQuality : string.Empty,
                TorrentName = status == TorrentOrderStatus.AddedToClient ? source.SelectedCandidateName : string.Empty
            };
            if (keepCandidates)
            {
                foreach (var candidate in source.Candidates)
                {
                    copy.Candidates.Add(candidate);
                }

                copy.SelectedCandidateId = source.SelectedCandidateId;
            }

            torrent.Orders.Add(copy);
        }
    }

    private void NavigateTo(AppWorkspaceKind kind)
    {
        var item = _mainViewModel.NavigationItems.First(navigationItem => navigationItem.Kind == kind);
        _mainViewModel.NavigateCommand.Execute(item);
    }

    private async Task CaptureSettingsSectionsAsync()
    {
        var settingsItem = _mainViewModel.SettingsItem;
        _mainViewModel.NavigateCommand.Execute(settingsItem);
        await SettleAsync(_settleMs);
        if (_mainViewModel.CurrentView is not SettingsViewModel settings)
        {
            _failed.Add(("settings", "System Settings did not open."));
            return;
        }

        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            await CaptureStepAsync(
                $"settings-{Slug(section.ToString())}",
                $"System Settings: {SplitWords(section.ToString())} section",
                async () =>
                {
                    settings.SelectedSettingsSection = section;
                    await SettleAsync(Math.Min(_settleMs, 1200));
                    return RenderWindow(_window);
                });
        }
    }

    private async Task CaptureExpandedSidebarAsync()
    {
        var news = _mainViewModel.NavigationItems.FirstOrDefault(item => item.Kind == AppWorkspaceKind.News);
        await CaptureStepAsync(
            "shell-sidebar-expanded",
            "Shell with the sidebar expanded (News)",
            async () =>
            {
                if (news is not null)
                {
                    _mainViewModel.NavigateCommand.Execute(news);
                }

                _mainViewModel.IsSidebarCollapsed = false;
                await SettleAsync(Math.Min(_settleMs, 1200));
                var image = RenderWindow(_window);
                _mainViewModel.IsSidebarCollapsed = true;
                return image;
            });
    }

    private async Task CaptureConsoleAsync()
    {
        await CaptureStepAsync(
            "console",
            "Console log window (shell Console button)",
            async () =>
            {
                _consoleWindowService.ShowConsole();
                await SettleAsync(Math.Min(_settleMs, 1500));
                var console = System.Windows.Application.Current.Windows.OfType<ConsoleLogWindow>().FirstOrDefault()
                              ?? throw new InvalidOperationException("Console window did not open.");
                var image = RenderWindow(console);
                console.CloseForShutdown();
                return image;
            });
    }

    private async Task CaptureDialogAsync(string name, string caption, Func<Window> createDialog)
    {
        await CaptureStepAsync(
            name,
            caption,
            async () =>
            {
                var dialog = createDialog();
                dialog.Owner = _window;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowActivated = false;
                dialog.Show();
                try
                {
                    await SettleAsync(Math.Min(_settleMs, 1200));
                    return RenderWindow(dialog);
                }
                finally
                {
                    dialog.Close();
                }
            });
    }

    private async Task CaptureCandidateListAsync()
    {
        await CaptureStepAsync(
            "dialog-candidate-list",
            "Cart candidate list (flyout of the candidate picker, placeholder data)",
            async () =>
            {
                var picker = new CartCandidatePickerControl
                {
                    DataContext = CreateSampleOrder(),
                    Width = 640,
                    Margin = new Thickness(16)
                };
                var host = new Window
                {
                    Owner = _window,
                    Title = "UI snapshot host",
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = picker
                };
                host.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AppBrushSurface");
                host.Show();
                try
                {
                    await SettleAsync(500);
                    if (picker.FindName("PickerToggle") is not ToggleButton toggle ||
                        picker.FindName("PopupCard") is not FrameworkElement card)
                    {
                        throw new InvalidOperationException("Candidate picker template changed: PickerToggle/PopupCard not found.");
                    }

                    toggle.IsChecked = true;
                    await SettleAsync(800);
                    return RenderPopupContent(card);
                }
                finally
                {
                    host.Close();
                }
            });
    }

    #endregion

    #region Placeholder data for dialogs

    private static Window CreateEpisodeOrganizationDialog()
        => new EpisodeOrganizationDialog(
            "Show 01",
            2,
            24,
            [
                new TmdbEpisodeGroupSummary { Id = "sample-a", Name = "Aired order (sample)", Type = 1, EpisodeCount = 24, GroupCount = 2 },
                new TmdbEpisodeGroupSummary { Id = "sample-b", Name = "Production order (sample)", Type = 6, EpisodeCount = 24, GroupCount = 1 }
            ]);

    private Window CreateDiskPlanDialog()
    {
        const long gb = 1024L * 1024 * 1024;
        var orders = new List<TorrentCartOrder>
        {
            new()
            {
                Id = -1, TargetKind = MediaKind.TvEpisode, MediaId = -1, SeasonNumber = 1,
                Title = "Show 01 S01", SelectedCandidateName = "Show.01.S01.1080p.WEB-DL.x265-SAMPLE",
                SelectedCandidateFileSize = 14 * gb, Status = TorrentOrderStatus.Approved
            },
            new()
            {
                Id = -2, TargetKind = MediaKind.TvEpisode, MediaId = -2, SeasonNumber = 2,
                Title = "Show 02 S02", SelectedCandidateName = "Show.02.S02.2160p.WEB-DL.DV.HDR-SAMPLE",
                SelectedCandidateFileSize = 38 * gb, Status = TorrentOrderStatus.Approved
            },
            new()
            {
                Id = -3, TargetKind = MediaKind.Movie, MediaId = -3,
                Title = "Movie 01", SelectedCandidateName = "Movie.01.2023.1080p.BluRay.x264-SAMPLE",
                SelectedCandidateFileSize = 9 * gb, Status = TorrentOrderStatus.Approved
            }
        };
        var plan = _diskAssignmentService.BuildPlan(orders, (_, _) => null);
        _diskAssignmentService.AutoAssign(plan);
        return new TorrentAddDiskDialog(plan, _diskAssignmentService, _downloadFolderCatalogService);
    }

    private static TorrentOrderViewModel CreateSampleOrder()
    {
        const long gb = 1024L * 1024 * 1024;
        var order = new TorrentOrderViewModel
        {
            Id = -1,
            TargetKind = MediaKind.TvEpisode,
            SeasonNumber = 1,
            Title = "Show 01 S01",
            Status = TorrentOrderStatus.Draft
        };
        var samples = new (string Name, string Quality, long Size, int Seeders, string Audio)[]
        {
            ("Show.01.S01.1080p.WEB-DL.DDP5.1.x265-SAMPLE", "1080p", 14 * gb, 212, "DDP5.1"),
            ("Show.01.S01.2160p.WEB-DL.DV.HDR.DDP5.1.Atmos-SAMPLE", "2160p", 41 * gb, 96, "DDP5.1 Atmos"),
            ("Show.01.S01.1080p.BluRay.REMUX.TrueHD.7.1-SAMPLE", "1080p", 62 * gb, 31, "TrueHD 7.1"),
            ("Show.01.S01.720p.WEB-DL.AAC2.0.x264-SAMPLE", "720p", 6 * gb, 18, "AAC2.0"),
            ("Show.01.S01-S02.1080p.WEB-DL.x265-SAMPLE", "1080p", 27 * gb, 7, "AAC2.0")
        };
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            order.Candidates.Add(new TorrentOrderCandidateViewModel(new TorrentCartOrderCandidate
            {
                Id = -(i + 1),
                OrderId = -1,
                Rank = i + 1,
                IsSelected = i == 0,
                Name = sample.Name,
                PluginName = "sample",
                FileSize = sample.Size,
                Seeders = sample.Seeders,
                Leechers = sample.Seeders / 4,
                Quality = sample.Quality,
                AudioCodec = sample.Audio,
                CoveredSeasons = i == samples.Length - 1 ? "1,2" : "1",
                TotalScore = 100 - (i * 7)
            }));
        }

        order.SelectedCandidateId = -1;
        return order;
    }

    #endregion

    #region Capture plumbing

    private async Task CaptureStepAsync(string name, string caption, Func<Task<BitmapSource>> capture)
    {
        _sequence++;
        var fileName = $"{_sequence:00}-{name}.png";
        try
        {
            var image = await capture();
            SavePng(image, Path.Combine(_outputFolder, fileName));
            _captured.Add((fileName, caption));
            _logger.Info($"UI snapshots: captured {fileName}.", LogTarget.File);
        }
        catch (Exception ex)
        {
            _failed.Add((fileName, ex.Message));
            _logger.Warning($"UI snapshots: {fileName} failed: {ex.Message}", LogTarget.All);
        }
    }

    private async Task SettleAsync(int delayMs)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
        }

        var deadline = DateTime.UtcNow + BusyWaitLimit;
        while (IsBusy(_mainViewModel.CurrentView) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        _window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static bool IsBusy(object? viewModel)
    {
        if (viewModel is null)
        {
            return false;
        }

        foreach (var property in viewModel.GetType().GetProperties())
        {
            if (property.PropertyType == typeof(bool) &&
                property.GetIndexParameters().Length == 0 &&
                BusyPropertyName().IsMatch(property.Name) &&
                property.GetValue(viewModel) is true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The window's template root: the whole client area at offset 0 (system chrome is not drawn).</summary>
    private static BitmapSource RenderWindow(Window window)
    {
        window.UpdateLayout();
        if (VisualTreeHelper.GetChildrenCount(window) == 0 ||
            VisualTreeHelper.GetChild(window, 0) is not FrameworkElement root)
        {
            throw new InvalidOperationException($"{window.GetType().Name} has no visual content.");
        }

        return RenderRoot(root);
    }

    /// <summary>The popup's own root (card plus its shadow margin), so nothing is cropped or shifted.</summary>
    private static BitmapSource RenderPopupContent(FrameworkElement popupChild)
    {
        if (PresentationSource.FromVisual(popupChild)?.RootVisual is not FrameworkElement root)
        {
            throw new InvalidOperationException("The popup is not open.");
        }

        return RenderRoot(root);
    }

    /// <summary>
    /// Renders at 96 DPI so one image pixel = one WPF unit: a 1280x720 window gives a 1280x720 PNG whatever
    /// the Windows display scale is. Only for roots (visual offset 0): RenderTargetBitmap draws a child's
    /// margin offset and would crop it.
    /// </summary>
    private static BitmapSource RenderRoot(FrameworkElement root)
    {
        root.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void WriteIndex()
    {
        var commit = _commit;
        var builder = new StringBuilder();
        builder.AppendLine("# UI snapshots");
        builder.AppendLine();
        builder.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd HH:mm} (local)");
        builder.AppendLine($"- Commit: {(string.IsNullOrWhiteSpace(commit) ? "(not given)" : commit)}");
        builder.AppendLine($"- App version: {AppVersionInfo.Informational}");
        builder.AppendLine($"- Design size: {DesignWidth:0}x{DesignHeight:0}; theme from the sandbox settings");
        builder.AppendLine("- State: sandbox copy, external services cut off (see docs/UI_SNAPSHOTS.md). Contains real library titles: local only, never commit.");
        builder.AppendLine();
        builder.AppendLine("## Images");
        builder.AppendLine();
        foreach (var (file, caption) in _captured)
        {
            builder.AppendLine($"- [{file}]({file}): {caption}");
        }

        if (_failed.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failed");
            builder.AppendLine();
            foreach (var (name, error) in _failed)
            {
                builder.AppendLine($"- {name}: {error}");
            }
        }

        File.WriteAllText(Path.Combine(_outputFolder, "index.md"), builder.ToString());
    }

    private static string Slug(string text)
        => SlugInvalid().Replace(SplitWords(text).ToLowerInvariant(), "-").Trim('-');

    private static string SplitWords(string text)
        => CamelBoundary().Replace(text, " $1");

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugInvalid();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex("^Is(\\w*Busy|Loading\\w*|\\w*Loading)$")]
    private static partial Regex BusyPropertyName();

    #endregion
}
