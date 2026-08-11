using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Utils;
using SafeDiskCleaner.Core.Windows;
using SafeDiskCleaner.Infrastructure;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var host = Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration((ctx, config) => config.AddJsonFile("appsettings.json", optional: true))
    .ConfigureServices((ctx, services) =>
    {
        services.AddSafeDiskInfrastructure(ctx.Configuration);
        services.AddSafeDiskDatabase();
        services.AddSingleton<SignatureInspector>();
        services.AddSingleton<SafetyValidator>();
        services.AddSingleton<Scanner>();
        services.AddSingleton<CleanupEngine>();
    })
    .UseSerilogDefaults()
    .Build();

await host.StartAsync();

var exitCode = await RunAsync(host.Services, args);
await host.StopAsync();
host.Dispose();
return exitCode;

static async Task<int> RunAsync(IServiceProvider services, string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 0;
    }

    var scanner = services.GetRequiredService<Scanner>();
    var cleanup = services.GetRequiredService<CleanupEngine>();
    var quarantine = services.GetRequiredService<SafeDiskCleaner.Core.Abstractions.IQuarantineService>();
    var audit = services.GetRequiredService<SafeDiskCleaner.Core.Abstractions.IAuditService>();
    var update = services.GetRequiredService<SafeDiskCleaner.Core.Abstractions.IUpdateService>();
    var reports = services.GetRequiredService<SafeDiskCleaner.Core.Abstractions.IReportWriter>();

    return args[0].ToLowerInvariant() switch
    {
        "analyze" => await AnalyzeAsync(scanner, reports, args[1..]),
        "clean" => await CleanAsync(scanner, cleanup, reports, args[1..]),
        "duplicates" => await DuplicatesAsync(scanner, args[1..]),
        "drives" => ListDrives(),
        "audit" => await ShowAuditAsync(audit),
        "quarantine" => await QuarantineAsync(quarantine, args[1..]),
        "update" => await CheckUpdateAsync(update),
        "-h" or "--help" or "help" => PrintUsage(),
        _ => UnknownCommand(args[0]),
    };
}

static int PrintUsage()
{
    Console.WriteLine("""
        SafeDisk Cleaner CLI

        Usage:
          SafeDiskCleaner.Cli analyze   [--roots d1,d2] [--medium] [--advanced] [--min-confidence N] [--recency-days N] [--report]
          SafeDiskCleaner.Cli clean     [--auto | --dry-run] [--roots d1,d2] [--medium] [--advanced] [--min-confidence N] [--recency-days N] [--report]
          SafeDiskCleaner.Cli duplicates --roots d1,d2
          SafeDiskCleaner.Cli drives
          SafeDiskCleaner.Cli audit
          SafeDiskCleaner.Cli quarantine list|restore <id>|remove <id>|purge
          SafeDiskCleaner.Cli update
        """);
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Невідома команда: {command}");
    PrintUsage();
    return 1;
}

static ScanOptions BuildScanOptions(IReadOnlyList<string> args)
{
    var rootValues = Arg(args, "--roots");
    var minConfidence = Arg(args, "--min-confidence");
    var recencyDays = Arg(args, "--recency-days");

    return new ScanOptions
    {
        Roots = string.IsNullOrWhiteSpace(rootValues)
            ? Array.Empty<string>()
            : rootValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        IncludeMedium = args.Contains("--medium"),
        IncludeAdvanced = args.Contains("--advanced"),
        MinConfidence = byte.TryParse(minConfidence, out var mc) ? mc : (byte)50,
        RecencyDays = uint.TryParse(recencyDays, out var rd) ? rd : 7u,
    };
}

static string? Arg(IReadOnlyList<string> args, string name)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return null;
}

static async Task<int> AnalyzeAsync(Scanner scanner, SafeDiskCleaner.Core.Abstractions.IReportWriter reports, string[] args)
{
    var options = BuildScanOptions(args);
    var result = await Task.Run(() => scanner.Scan(options, null, CancellationToken.None));

    Console.WriteLine($"Проскановано файлів: {result.Summary.ScannedFiles:N0}, кандидатів: {result.Candidates.Count}");
    Console.WriteLine($"Потенційно звільниться: {HumanSize.Format(result.Summary.TotalPotential)}");
    Console.WriteLine();

    foreach (var c in result.Candidates.Take(20))
    {
        Console.WriteLine($"[{c.Confidence,3}%] {HumanSize.Format(c.Size),10} {c.Category.Label(),-18} {c.Path} — {c.Reason}");
    }

    if (result.Candidates.Count > 20)
    {
        Console.WriteLine($"... ще {result.Candidates.Count - 20} кандидатів.");
    }

    if (args.Contains("--report"))
    {
        var file = await reports.WriteScanReportAsync(result);
        Console.WriteLine($"Звіт збережено: {file}");
    }

    return 0;
}

static async Task<int> CleanAsync(Scanner scanner, CleanupEngine cleanup, SafeDiskCleaner.Core.Abstractions.IReportWriter reports, string[] args)
{
    var options = BuildScanOptions(args);
    var scanResult = await Task.Run(() => scanner.Scan(options, null, CancellationToken.None));

    var mode = args.Contains("--dry-run")
        ? CleanMode.DryRun
        : args.Contains("--auto")
            ? CleanMode.Auto
            : CleanMode.Interactive;

    var cleanOptions = new CleanupOptions { Mode = mode };
    var candidates = scanResult.Candidates
        .Where(c => c.Action != CandidateAction.Keep)
        .ToList();

    Console.WriteLine($"Кандидатів до обробки: {candidates.Count} ({mode})");

    CleanupResult result;
    if (mode == CleanMode.Interactive)
    {
        var approved = new List<Candidate>();
        foreach (var c in candidates)
        {
            Console.Write($"[{c.Confidence,3}%] {HumanSize.Format(c.Size),10} {c.Path}? [y/N/a=all/d=skip-all] ");
            var key = Console.ReadKey();
            Console.WriteLine();
            var choice = char.ToLowerInvariant(key.KeyChar);
            if (choice == 'a')
            {
                approved.AddRange(candidates.Skip(candidates.IndexOf(c)));
                break;
            }

            if (choice == 'd')
            {
                break;
            }

            if (choice == 'y')
            {
                approved.Add(c);
            }
        }

        result = await cleanup.RunAsync(approved, cleanOptions, null);
    }
    else
    {
        result = await cleanup.RunAsync(candidates, cleanOptions, null);
    }

    Console.WriteLine($"Оброблено: {result.Processed}, звільнено: {HumanSize.Format(result.FreedBytes)}");
    foreach (var entry in result.Entries)
    {
        Console.WriteLine($"[{entry.Status}] {entry.Path} — {entry.Detail}");
    }

    if (args.Contains("--report"))
    {
        var file = await reports.WriteCleanupReportAsync(result);
        Console.WriteLine($"Звіт збережено: {file}");
    }

    return 0;
}

static async Task<int> DuplicatesAsync(Scanner scanner, string[] args)
{
    var rootsValue = Arg(args, "--roots");
    if (string.IsNullOrWhiteSpace(rootsValue))
    {
        Console.Error.WriteLine("Для пошуку дублікатів вкажіть --roots d1,d2");
        return 1;
    }

    var roots = rootsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var result = await Task.Run(() => scanner.ScanDuplicates(roots, CancellationToken.None));

    Console.WriteLine($"Дублікатів знайдено: {result.Candidates.Count}");
    foreach (var c in result.Candidates.Take(50))
    {
        Console.WriteLine($"{HumanSize.Format(c.Size),10} {c.Path} — {c.Reason}");
    }

    return 0;
}

static int ListDrives()
{
    foreach (var drive in WindowsApi.ListDrives())
    {
        Console.WriteLine($"{drive.Letter,-3} {drive.Kind,-10} total={HumanSize.Format((long)drive.Total),12} free={HumanSize.Format((long)drive.Free),12}");
    }

    return 0;
}

static async Task<int> ShowAuditAsync(SafeDiskCleaner.Core.Abstractions.IAuditService audit)
{
    var entries = await audit.GetAllAsync();
    Console.WriteLine($"Записів: {entries.Count}");
    foreach (var e in entries.Take(50))
    {
        Console.WriteLine($"{e.Timestamp:yyyy-MM-dd HH:mm} [{e.Action,-12}] {(e.Success ? "OK " : "ERR")} {HumanSize.Format(e.Size),10} {e.Path}");
    }

    return 0;
}

static async Task<int> QuarantineAsync(SafeDiskCleaner.Core.Abstractions.IQuarantineService quarantine, string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("quarantine: вкажіть list|restore <id>|remove <id>|purge");
        return 1;
    }

    return args[0].ToLowerInvariant() switch
    {
        "list" => await QuarantineListAsync(quarantine),
        "restore" => await QuarantineActionAsync(quarantine, args, id => quarantine.RestoreAsync(id)),
        "remove" => await QuarantineActionAsync(quarantine, args, id => quarantine.RemoveAsync(id)),
        "purge" => await QuarantinePurgeAsync(quarantine),
        _ => UnknownCommand(args[0]),
    };
}

static async Task<int> QuarantineListAsync(SafeDiskCleaner.Core.Abstractions.IQuarantineService quarantine)
{
    var entries = await quarantine.ListAsync();
    foreach (var e in entries)
    {
        Console.WriteLine($"{e.Id}  {e.QuarantinedAt:yyyy-MM-dd}  {HumanSize.Format(e.Size),10}  {e.OriginalPath}");
    }

    return 0;
}

static async Task<int> QuarantineActionAsync(
    SafeDiskCleaner.Core.Abstractions.IQuarantineService quarantine,
    string[] args,
    Func<string, Task> action)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Вкажіть id карантинного запису");
        return 1;
    }

    try
    {
        await action(args[1]);
        Console.WriteLine("OK");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<int> QuarantinePurgeAsync(SafeDiskCleaner.Core.Abstractions.IQuarantineService quarantine)
{
    var purged = await quarantine.PurgeExpiredAsync();
    Console.WriteLine($"Видалено прострочених записів: {purged}");
    return 0;
}

static async Task<int> CheckUpdateAsync(SafeDiskCleaner.Core.Abstractions.IUpdateService update)
{
    var info = await update.CheckAsync();
    Console.WriteLine(info.Available
        ? $"Доступна версія: {info.LatestVersion} ({info.DownloadUrl})"
        : $"Встановлена остання версія ({info.CurrentVersion})");
    return 0;
}
