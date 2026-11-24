using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.UiSnapshot;

namespace MediaManager.App.Tests.Startup;

public class UiSnapshotModeTests
{
    private const string Sandbox = @"C:\Temp\MediaManagerUiSnapshot\state";
    private const string Output = @"C:\Temp\ui-snapshots\run";

    [Fact]
    public void TryParse_ReadsSnapshotFolderWithStateFolder()
    {
        var ok = AppLaunchOptions.TryParse(
            ["--state-folder", Sandbox, "--ui-snapshots", Output],
            out var options,
            out var error);

        Assert.True(ok, error);
        Assert.True(options.IsUiSnapshotMode);
        Assert.Equal(Output, options.UiSnapshotOutputFolder);
        Assert.True(options.SafeTestMode);
        Assert.DoesNotContain(AppLaunchOptions.UiSnapshotsFlag, options.ToRestartArguments());
    }

    [Fact]
    public void TryParse_AcceptsEqualsForm()
    {
        var ok = AppLaunchOptions.TryParse(
            [$"--state-folder={Sandbox}", $"--ui-snapshots={Output}"],
            out var options,
            out _);

        Assert.True(ok);
        Assert.Equal(Output, options.UiSnapshotOutputFolder);
    }

    [Fact]
    public void TryParse_RefusesSnapshotsWithoutStateFolder()
    {
        var ok = AppLaunchOptions.TryParse(["--ui-snapshots", Output], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--state-folder", error);
    }

    [Fact]
    public void TryParse_RefusesSnapshotsWithEnableBackground()
    {
        var ok = AppLaunchOptions.TryParse(
            ["--state-folder", Sandbox, "--ui-snapshots", Output, "--enable-background"],
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("--enable-background", error);
    }

    [Fact]
    public void TryParse_RefusesSnapshotsWithoutFolder()
    {
        var ok = AppLaunchOptions.TryParse(["--state-folder", Sandbox, "--ui-snapshots"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--ui-snapshots", error);
    }

    [Fact]
    public void TryParse_WithoutFlag_IsNotSnapshotMode()
    {
        Assert.True(AppLaunchOptions.TryParse(["--state-folder", Sandbox], out var options, out _));
        Assert.False(options.IsUiSnapshotMode);
    }

    [Theory]
    [InlineData(AppConstants.LegacyStateFolder)]
    [InlineData(AppConstants.LegacyStateFolder + @"\")]
    [InlineData(AppConstants.LegacyStateFolder + @"\nested")]
    public void Refusal_BlocksLegacyLiveFolder(string folder)
    {
        Assert.NotNull(UiSnapshotSandbox.GetStateFolderRefusal(folder, pointerTarget: null));
    }

    [Fact]
    public void Refusal_BlocksDefaultAndPointerFolders()
    {
        Assert.NotNull(UiSnapshotSandbox.GetStateFolderRefusal(AppConstants.DefaultStateFolder, pointerTarget: null));
        Assert.NotNull(UiSnapshotSandbox.GetStateFolderRefusal(@"E:\LiveState", pointerTarget: @"e:\livestate\"));
    }

    [Fact]
    public void Refusal_AllowsSandboxAndSiblingNames()
    {
        Assert.Null(UiSnapshotSandbox.GetStateFolderRefusal(Sandbox, pointerTarget: @"E:\LiveState"));
        Assert.Null(UiSnapshotSandbox.GetStateFolderRefusal(AppConstants.LegacyStateFolder + "-sandbox", pointerTarget: null));
        Assert.NotNull(UiSnapshotSandbox.GetStateFolderRefusal(" ", pointerTarget: null));
    }

    [Fact]
    public void CutOffExternalServices_RemovesSecretsAndDisablesBackgroundWork()
    {
        var settings = new AppSettings
        {
            TmdbReadAccessToken = "tmdb",
            AutoTorrent = { QbittorrentWebUiUrl = "http://localhost:8080", Password = "pw", ApiKey = "key" },
            Gemini = { Enabled = true, ApiKey = "gemini" },
            Backup = { Enabled = true, CredentialsFilePath = @"D:\secret.json" },
            Startup = { RunAtStartup = true, StartMinimized = true, CloseToTray = true },
            Notifications = { EnabledByKind = { ["SymlinkCreated"] = true } }
        };
        settings.AutoTrack.Enabled = true;
        settings.AutoTrack.Jellyfin.Enabled = true;
        settings.AutoTrack.Jellyfin.ApiKey = "jf";
        settings.Warp.Enabled = true;
        settings.Symlink.Enabled = true;
        settings.Symlink.UnifiedRoot = @"C:\JellyfinLibrary";

        UiSnapshotSandbox.CutOffExternalServices(settings, Sandbox);

        Assert.Null(settings.TmdbReadAccessToken);
        Assert.Equal(UiSnapshotSandbox.UnreachableUrl, settings.AutoTorrent.QbittorrentWebUiUrl);
        Assert.Null(settings.AutoTorrent.Password);
        Assert.Null(settings.AutoTorrent.ApiKey);
        Assert.False(settings.AutoTorrent.ProcessRestart.Enabled);
        Assert.False(settings.Gemini.Enabled);
        Assert.Null(settings.Gemini.ApiKey);
        Assert.False(settings.Backup.Enabled);
        Assert.Null(settings.Backup.CredentialsFilePath);
        Assert.False(settings.AutoTrack.Enabled);
        Assert.False(settings.AutoTrack.Jellyfin.Enabled);
        Assert.Null(settings.AutoTrack.Jellyfin.ApiKey);
        Assert.Equal(UiSnapshotSandbox.UnreachableUrl, settings.AutoTrack.Jellyfin.BaseUrl);
        Assert.False(settings.Warp.Enabled);
        Assert.StartsWith(Sandbox, settings.Warp.ExecutablePath);
        Assert.False(settings.Symlink.Enabled);
        Assert.StartsWith(Sandbox, settings.Symlink.UnifiedRoot);
        Assert.False(settings.Startup.RunAtStartup);
        Assert.False(settings.Startup.StartMinimized);
        Assert.False(settings.Startup.CloseToTray);
        Assert.True(settings.Startup.SetupCompleted);
        Assert.False(settings.Notifications.EnabledByKind["SymlinkCreated"]);
    }

    [Fact]
    public void ResetTransientUiState_ClearsListFilters()
    {
        var settings = new AppSettings
        {
            Ui =
            {
                LibraryMediaSearchQuery = "abc",
                TorrentMediaSearchQuery = "def",
                LibraryWatchStatusFilters = [UserWatchStatus.None],
                LibraryWatchStatusFilter = UserWatchStatus.None,
                TorrentWatchStatusFilters = [UserWatchStatus.None]
            }
        };

        UiSnapshotSandbox.ResetTransientUiState(settings);

        Assert.Equal(string.Empty, settings.Ui.LibraryMediaSearchQuery);
        Assert.Equal(string.Empty, settings.Ui.TorrentMediaSearchQuery);
        Assert.Null(settings.Ui.LibraryWatchStatusFilters);
        Assert.Null(settings.Ui.LibraryWatchStatusFilter);
        Assert.Null(settings.Ui.TorrentWatchStatusFilters);
    }
}
