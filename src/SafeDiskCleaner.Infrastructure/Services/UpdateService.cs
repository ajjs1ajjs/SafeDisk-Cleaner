using Microsoft.Extensions.Logging;
using Refit;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed class UpdateService : IUpdateService
{
    private const string Repo = "ajjs1ajjs/SafeDisk-Cleaner";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly IGitHubApi _api;
    private readonly ILogger<UpdateService> _logger;
    private readonly string _currentVersion;

    private UpdateInfo? _cached;
    private DateTimeOffset _cacheExpiry;

    public UpdateService(IGitHubApi api, ILogger<UpdateService> logger)
    {
        _api = api;
        _logger = logger;
        _currentVersion = typeof(UpdateService).Assembly
            .GetName()
            .Version?
            .ToString(3) ?? "0.0.0";
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow < _cacheExpiry)
        {
            return _cached;
        }

        try
        {
            var release = await _api.GetLatestRelease(Repo, ct);
            var tag = release.TagName;
            var htmlUrl = release.HtmlUrl;

            var available = !string.IsNullOrWhiteSpace(tag)
                && !string.IsNullOrWhiteSpace(htmlUrl)
                && SemanticVersion.IsNewerThan(tag, _currentVersion);

            _cached = new UpdateInfo
            {
                Available = available,
                LatestVersion = tag ?? string.Empty,
                CurrentVersion = _currentVersion,
                DownloadUrl = htmlUrl ?? string.Empty,
            };
            _cacheExpiry = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cached;
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Update check failed");
            return new UpdateInfo
            {
                Available = false,
                LatestVersion = string.Empty,
                CurrentVersion = _currentVersion,
                DownloadUrl = string.Empty,
            };
        }
    }
}
