namespace SafeDiskCleaner.Core.Abstractions;

public enum ScheduleFrequency
{
    Daily,
    Weekly,
}

/// <summary>Describes an auto-clean scheduled task.</summary>
public sealed record ScheduleOptions
{
    /// <summary>Full path of the executable to launch (the CLI host).</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Command-line arguments passed to the executable.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Time of day in "HH:mm" format.</summary>
    public string TimeOfDay { get; init; } = "03:00";

    public ScheduleFrequency Frequency { get; init; } = ScheduleFrequency.Daily;
}

/// <summary>
/// Registers/removes an OS-level scheduled task that runs the auto-clean.
/// Windows uses schtasks, Linux a systemd user timer, macOS a launchd agent.
/// </summary>
public interface IScheduleService
{
    bool IsSupported { get; }

    /// <summary>True when the scheduled task is currently registered.</summary>
    Task<bool> IsRegisteredAsync();

    /// <summary>Creates or updates the scheduled task. Throws on failure.</summary>
    Task ApplyAsync(ScheduleOptions options);

    /// <summary>Removes the scheduled task if present.</summary>
    Task RemoveAsync();
}
