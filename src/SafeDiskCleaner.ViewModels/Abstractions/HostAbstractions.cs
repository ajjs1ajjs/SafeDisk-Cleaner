namespace SafeDiskCleaner.ViewModels.Abstractions;

/// <summary>Marshals work onto the host UI thread (WPF Dispatcher / Avalonia UIThread).</summary>
public interface IDispatcher
{
    bool CheckAccess();

    void Invoke(Action action);
}

/// <summary>Applies the dark/light base theme and accent preset; implemented per UI framework.</summary>
public interface IThemeService
{
    void Apply(bool dark, string accent);
}

/// <summary>UI-thread timer abstraction over WPF DispatcherTimer / Avalonia DispatcherTimer.</summary>
public interface IUiTimer : IDisposable
{
    event EventHandler? Tick;

    void Start(TimeSpan interval);

    void Stop();
}

/// <summary>Lifecycle control implemented by each host (shutdown the application).</summary>
public interface IAppLifecycle
{
    /// <summary>Requests an orderly application shutdown on the UI thread.</summary>
    Task ShutdownAsync();
}

/// <summary>
/// Checks for a new release, downloads it and launches the installer flow.
/// Implemented per host (the install script differs between frameworks).
/// </summary>
public interface IUpdateInstaller
{
    Task<Core.Models.UpdateInfo> CheckAsync(CancellationToken ct = default);

    Core.Models.ReleaseAsset? SelectAsset(Core.Models.UpdateInfo info);

    Task DownloadAsync(
        Core.Models.ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct = default);

    void LaunchInstaller(string downloadedPath);

    /// <summary>Finds the "&lt;asset&gt;.sha256" companion asset, or null when the release ships none.</summary>
    Core.Models.ReleaseAsset? SelectChecksumAsset(Core.Models.UpdateInfo info);

    /// <summary>Downloads a small text asset (e.g. a checksum file).</summary>
    Task<string> DownloadTextAsync(Core.Models.ReleaseAsset asset, CancellationToken ct = default);

    /// <summary>
    /// Verifies the SHA-256 of the downloaded file against a checksum-file
    /// payload. Throws and deletes the file when verification fails.
    /// </summary>
    void VerifySha256(string downloadedPath, string checksumPayload);
}
