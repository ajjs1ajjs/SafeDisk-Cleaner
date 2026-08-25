using FluentAssertions;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Platform;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Tests;

public sealed class ScheduleAndAppsTests
{
    // ---------- schtasks builders ----------

    [Fact]
    public void SchTasks_DailyArgs_ContainTaskNameTimeAndCommand()
    {
        var args = ScheduleService.BuildSchTasksCreateArgs(new ScheduleOptions
        {
            ExecutablePath = @"C:\App\SafeDiskCleaner.Cli.exe",
            Arguments = "clean --auto",
            TimeOfDay = "04:30",
            Frequency = ScheduleFrequency.Daily,
        });

        args.Should().Contain("/Create");
        args.Should().Contain("/TN SafeDiskCleanerAutoClean");
        args.Should().Contain("/SC DAILY");
        args.Should().Contain("/ST 04:30");
        args.Should().Contain("SafeDiskCleaner.Cli.exe clean --auto");
    }

    [Fact]
    public void SchTasks_WeeklyArgs_IncludeCurrentWeekday()
    {
        var expected = DateTime.Today.DayOfWeek switch
        {
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            _ => "SUN",
        };

        var args = ScheduleService.BuildSchTasksCreateArgs(new ScheduleOptions
        {
            ExecutablePath = "cli",
            TimeOfDay = "12:00",
            Frequency = ScheduleFrequency.Weekly,
        });

        args.Should().Contain($"/SC WEEKLY /D {expected}");
    }

    [Theory]
    [InlineData("3:5", "03:05")]
    [InlineData("23:59", "23:59")]
    [InlineData("bogus", "03:00")]
    public void NormalizeTime_FallsBackGracefully(string input, string expected)
    {
        ScheduleService.NormalizeTime(input).Should().Be(expected);
    }

    // ---------- systemd / launchd payloads ----------

    [Fact]
    public void SystemdUnits_ReferenceExecutable_AndOnCalendar()
    {
        var service = ScheduleService.BuildSystemdServiceUnit(@"/opt/sdc/SafeDiskCleaner.Cli", "clean --auto");
        service.Should().Contain("ExecStart=/opt/sdc/SafeDiskCleaner.Cli clean --auto");

        var timer = ScheduleService.BuildSystemdTimerUnit("02:15");
        timer.Should().Contain("OnCalendar=*-*-* 02:15:00");
        timer.Should().Contain("Persistent=true");
    }

    [Fact]
    public void LaunchdPlist_ListsAllArguments_AndCalendar()
    {
        var plist = ScheduleService.BuildLaunchdPlist(
            "SafeDiskCleanerAutoClean", "/Applications/sdc/cli", ["clean", "--auto"], 9, 5);

        plist.Should().Contain("<key>Label</key>");
        plist.Should().Contain("<string>SafeDiskCleanerAutoClean</string>");
        plist.Should().Contain("<string>/Applications/sdc/cli</string>");
        plist.Should().Contain("<string>clean</string>");
        plist.Should().Contain("<string>--auto</string>");
        plist.Should().Contain("<integer>9</integer>");
        plist.Should().Contain("<integer>5</integer>");
    }

    // ---------- installed apps command splitting ----------

    [Fact]
    public void TrySplitCommand_HandlesQuotedPaths()
    {
        var ok = InstalledAppsReader.TrySplitCommand(
            @"""C:\Program Files\App\unins000.exe"" /SILENT", out var exe, out var args);

        ok.Should().BeTrue();
        exe.Should().Be(@"C:\Program Files\App\unins000.exe");
        args.Should().Be("/SILENT");
    }

    [Fact]
    public void TrySplitCommand_HandlesMsiExec()
    {
        var ok = InstalledAppsReader.TrySplitCommand(
            @"MsiExec.exe /I{1234-ABCD}", out var exe, out var args);

        ok.Should().BeTrue();
        exe.Should().Be(@"MsiExec.exe");
        args.Should().Be("/I{1234-ABCD}");
    }

    [Fact]
    public void TrySplitCommand_RejectsEmpty()
    {
        InstalledAppsReader.TrySplitCommand("", out _, out _).Should().BeFalse();
        InstalledAppsReader.TrySplitCommand("   ", out _, out _).Should().BeFalse();
    }
}
