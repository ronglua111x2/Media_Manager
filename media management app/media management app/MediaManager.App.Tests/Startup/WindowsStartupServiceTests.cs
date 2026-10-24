using media_management_app.Services;

namespace MediaManager.App.Tests.Startup;

public class WindowsStartupServiceTests
{
    private const string InstalledExe = @"D:\App-Disk-D\Media_Manager\App\media management app.exe";
    private const string DevExe =
        @"d:\VScode\Misc\Media_Manager\media management app\media management app\bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\media management app.exe";

    // Task as registered by the old `schtasks /create /sc ONLOGON` call (copied from the live machine, 2026-10-15).
    private const string LegacySchtasksXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Settings>
            <DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"d:\VScode\Misc\Media_Manager\media management app\media management app\bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\media management app.exe"</Command>
            </Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void BuildTaskXml_AllowsBatteryAndHasNoTimeLimit()
    {
        var xml = WindowsStartupService.BuildTaskXml(InstalledExe, @"PC\user", new DateTime(2026, 10, 15, 9, 0, 0));
        var parsed = WindowsStartupService.ParseTaskXml(xml);

        Assert.Equal(InstalledExe, parsed.Command);
        Assert.False(parsed.DisallowStartIfOnBatteries);
        Assert.False(parsed.StopIfGoingOnBatteries);
        Assert.Equal("PT0S", parsed.ExecutionTimeLimit);
        Assert.Null(WindowsStartupService.GetReregisterReason(parsed, InstalledExe));
        Assert.Contains("<Priority>5</Priority>", xml);
        Assert.Contains("<UserId>PC\\user</UserId>", xml);
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void BuildTaskXml_EscapesPath()
    {
        var xml = WindowsStartupService.BuildTaskXml(@"C:\A&B\app.exe", "u", DateTime.Now);

        Assert.Contains(@"<Command>C:\A&amp;B\app.exe</Command>", xml);
        Assert.Equal(@"C:\A&B\app.exe", WindowsStartupService.ParseTaskXml(xml).Command);
    }

    [Fact]
    public void LegacyTask_IsReregistered()
    {
        var legacy = WindowsStartupService.ParseTaskXml(LegacySchtasksXml);

        Assert.Equal(DevExe, legacy.Command);
        Assert.True(legacy.DisallowStartIfOnBatteries);
        Assert.True(legacy.StopIfGoingOnBatteries);
        Assert.Equal("PT72H", legacy.ExecutionTimeLimit);
        Assert.Contains("pointed at", WindowsStartupService.GetReregisterReason(legacy, InstalledExe));
        Assert.Contains("battery", WindowsStartupService.GetReregisterReason(legacy, DevExe));
    }

    [Fact]
    public void MissingTask_IsReregistered()
    {
        Assert.Equal("task was missing", WindowsStartupService.GetReregisterReason(null, InstalledExe));
    }

    [Theory]
    [InlineData(DevExe, true)]
    [InlineData(@"C:\src\app\bin\Debug\net8.0-windows\app.exe", true)]
    [InlineData(InstalledExe, false)]
    [InlineData(@"C:\Program Files\bin\app.exe", false)]
    public void IsDevBuildPath_DetectsBuildOutput(string path, bool expected)
    {
        Assert.Equal(expected, WindowsStartupService.IsDevBuildPath(path));
    }
}
