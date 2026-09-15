using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.Core.Localization;
using SafeDiskCleaner.Core.Utils;
using SafeDiskCleaner.Core.Windows;
using SafeDiskCleaner.ViewModels.Abstractions;

namespace SafeDiskCleaner.ViewModels;

public sealed record AppRow(
    string Name,
    string Version,
    string Publisher,
    string SizeText,
    string UninstallString,
    string QuietUninstallString);

/// <summary>
/// Lists installed applications (Windows uninstall registry) and launches
/// their official uninstallers — quiet variant first, interactive fallback.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public ObservableCollection<AppRow> Apps { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _message;

    public bool IsSupported => OperatingSystem.IsWindows();

    public AppsViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public async Task RefreshAsync()
    {
        if (!IsSupported)
        {
            Message = Loc.T("Apps.WindowsOnly");
            return;
        }

        IsLoading = true;
        Message = null;
        try
        {
            Apps.Clear();
            await Task.Run(() =>
            {
                foreach (var app in InstalledAppsReader.ListInstalled())
                {
                    Apps.Add(new AppRow(
                        app.Name,
                        app.Version,
                        app.Publisher,
                        app.EstimatedSizeKb > 0 ? HumanSize.Format(app.EstimatedSizeKb * 1024) : "—",
                        app.UninstallString,
                        app.QuietUninstallString));
                }
            });

            if (Apps.Count == 0)
            {
                Message = Loc.T("Apps.Empty");
            }
        }
        catch (Exception ex)
        {
            Message = Loc.F("Common.Error", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(AppRow? row)
    {
        if (row is null)
        {
            return;
        }

          try
          {
            // Prefer the vendor-provided quiet uninstaller when present.
            var command = !string.IsNullOrWhiteSpace(row.QuietUninstallString)
                ? row.QuietUninstallString
                : row.UninstallString;

            if (!InstalledAppsReader.TrySplitCommand(command, out var exe, out var args))
            {
                Message = Loc.F("Apps.Error", "Unrecognized uninstall command format.");
                return;
            }

            // Registry uninstall strings are attacker-influenceable (HKCU is
            // user-writable): the target must exist, no raw-command fallback.
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                Message = Loc.F("Apps.Error", "Uninstaller executable not found.");
                return;
            }

            var confirmed = await _dialogs.ConfirmAsync(
                Loc.T("Apps.ConfirmTitle"),
                Loc.F("Apps.ConfirmCommand", row.Name, exe, args),
                Loc.T("Common.Delete"));
            if (!confirmed)
            {
                return;
            }

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
            };
            // msiexec-style commands need elevation semantics that only the
            // shell provides; keep it off otherwise (no PATH search games).
            if (exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                psi.UseShellExecute = true;
            }

            Process.Start(psi);

            Message = Loc.F("Apps.Launched", row.Name);
          }
        catch (Exception ex)
        {
            Message = Loc.F("Apps.Error", ex.Message);
        }
    }
}
