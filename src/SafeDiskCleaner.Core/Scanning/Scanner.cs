using SafeDiskCleaner.Core.Confidence;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Platform;
using SafeDiskCleaner.Core.Rules;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Scanning;

public sealed class Scanner
{
    public const ulong ProgressEveryFiles = 200;
    public const ulong ProgressWindowFiles = 2000;

    private const long DuplicateMinSize = 4096;

    private readonly IRecycleBin _recycleBin;

    public Scanner(IRecycleBin? recycleBin = null)
    {
        _recycleBin = recycleBin ?? PlatformServices.RecycleBin;
    }

    public static IReadOnlyList<string> DefaultScanRoots(bool includeMedium, bool includeAdvanced)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = new HashSet<string>(comparer);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsScanRoots(roots, includeMedium, includeAdvanced);
        }
        else
        {
            AddUnixScanRoots(roots, includeMedium);
        }

        return roots
            .Where(r => Directory.Exists(r))
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddWindowsScanRoots(HashSet<string> roots, bool includeMedium, bool includeAdvanced)
    {
        var temp = Environment.GetEnvironmentVariable("TEMP") ?? Environment.GetEnvironmentVariable("TMP");
        if (!string.IsNullOrWhiteSpace(temp))
        {
            roots.Add(temp);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            foreach (var sub in new[]
            {
                @"\CrashDumps",
                @"\Google\Chrome\User Data\Default\Cache",
                @"\Google\Chrome\User Data\Default\Code Cache",
                @"\Google\Chrome\User Data\Crashpad\reports",
                @"\Microsoft\Edge\User Data\Default\Cache",
                @"\Microsoft\Edge\User Data\Default\Code Cache",
                @"\Microsoft\Edge\User Data\Crashpad\reports",
                @"\BraveSoftware\Brave-Browser\User Data\Default\Cache",
                @"\BraveSoftware\Brave-Browser\User Data\Default\Code Cache",
                @"\Vivaldi\User Data\Default\Cache",
                @"\Vivaldi\User Data\Default\Code Cache",
                @"\Microsoft\Windows\Explorer",
                @"\NuGet\Cache",
                @"\npm-cache",
                @"\pip\cache",
                @"\Mozilla\Firefox\Profiles",
                @"\Yarn\Cache",
                @"\pnpm",
                @"\uv\cache",
                @"\go-build",
            })
            {
                roots.Add(local + sub);
            }
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roaming))
        {
            foreach (var sub in new[]
            {
                @"\discord\cache",
                @"\discord\code cache",
                @"\discord\gpucache",
                @"\discord\service worker",
                @"\slack\cache",
                @"\slack\code cache",
                @"\slack\gpucache",
                @"\microsoft\teams\cache",
                @"\microsoft\teams\code cache",
                @"\microsoft\teams\gpucache",
                @"\microsoft\teams\service worker",
                @"\whatsapp\cache",
                @"\whatsapp\code cache",
                @"\whatsapp\gpucache",
                @"\postman\cache",
                @"\figma\cache",
                @"\Opera Software\Opera Stable\Cache",
            })
            {
                roots.Add(roaming + sub);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            foreach (var sub in new[]
            {
                @"\.cargo\registry\cache",
                @"\.gradle\caches",
                @"\.yarn\berry",
                @"\.bun\install\cache",
            })
            {
                roots.Add(profile + sub);
            }
        }

        var windows = Environment.GetEnvironmentVariable("WINDIR")
            ?? Path.GetDirectoryName(Environment.SystemDirectory)
            ?? @"C:\Windows";
        var systemDrive = Path.GetPathRoot(windows) ?? @"C:\";

        roots.Add(Path.Combine(windows, "Temp"));
        if (includeMedium)
        {
            roots.Add(Path.Combine(windows, "SoftwareDistribution", "Download"));
        }

        if (includeAdvanced)
        {
            foreach (var old in new[]
            {
                Path.Combine(systemDrive, "Windows.old"),
                Path.Combine(systemDrive, "Windows~old"),
            })
            {
                if (Directory.Exists(old))
                {
                    roots.Add(old);
                }
            }
        }
    }

    private static void AddUnixScanRoots(HashSet<string> roots, bool includeMedium)
    {
        var temp = Environment.GetEnvironmentVariable("TMPDIR")
            ?? Environment.GetEnvironmentVariable("TEMP")
            ?? Environment.GetEnvironmentVariable("TMP");
        if (!string.IsNullOrWhiteSpace(temp))
        {
            roots.Add(temp);
        }

        roots.Add("/tmp");
        if (includeMedium)
        {
            roots.Add("/var/tmp");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }

        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cache))
        {
            cache = OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Caches")
                : Path.Combine(home, ".cache");
        }

        foreach (var sub in new[]
        {
            "npm/_cacache",
            "pip",
            "yarn",
            "pnpm",
            "go-build",
            "ms-playwright",
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "microsoft-edge",
            "brave-browser",
            "vivaldi",
            "mozilla/firefox",
            "com.apple.Safari",
            ".cargo/registry/cache",
            ".gradle/caches",
            ".bun/install/cache",
            "pypa",
        })
        {
            roots.Add(Path.Combine(cache, sub));
        }
    }

    public static bool ShouldPrune(string directory) =>
        PathProtection.IsProtectedPath(directory);

    public ScanResult Scan(ScanOptions options, Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var roots = options.Roots.Count > 0 ? options.Roots.ToList() : DefaultScanRoots(options.IncludeMedium, options.IncludeAdvanced).ToList();
        var totalRoots = Math.Max(1, roots.Count);

        var results = new System.Collections.Concurrent.ConcurrentBag<(List<Candidate> Candidates, ulong Files, ulong Dirs)>();

        System.Threading.Tasks.Parallel.For(
            0,
            roots.Count,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                ct.ThrowIfCancellationRequested();
                results.Add(ScanOneRoot(roots[i], options, i, totalRoots, onProgress, ct));
            });

        ct.ThrowIfCancellationRequested();

        var candidates = results.SelectMany(r => r.Candidates).ToList();
        candidates.AddRange(SpecialCandidates(options, systemDriveRoot: GetSystemDriveRoot()));

        var scannedFiles = results.Aggregate(0ul, (a, r) => a + r.Files);
        var scannedDirs = results.Aggregate(0ul, (a, r) => a + r.Dirs);

        var catStats = new Dictionary<Category, (int Count, long Size, long Potential)>();
        foreach (var c in candidates)
        {
            var entry = catStats.GetValueOrDefault(c.Category);
            entry.Count++;
            entry.Size += c.Size;
            if (c.Action is CandidateAction.Delete or CandidateAction.Review)
            {
                entry.Potential += c.Size;
            }

            catStats[c.Category] = entry;
        }

        var categories = catStats
            .Select(kv => new CategoryStats
            {
                Category = kv.Key,
                RiskLevel = kv.Key.RiskLevel(),
                Count = kv.Value.Count,
                Size = kv.Value.Size,
                Potential = kv.Value.Potential,
            })
            .OrderByDescending(c => c.Potential)
            .ToList();

        candidates.Sort(static (a, b) =>
        {
            var byConfidence = b.Confidence.CompareTo(a.Confidence);
            return byConfidence != 0 ? byConfidence : b.Size.CompareTo(a.Size);
        });

        var totalPotential = candidates
            .Where(c => c.Action is CandidateAction.Delete or CandidateAction.Review)
            .Sum(c => c.Size);

        onProgress?.Invoke(new ScanProgress
        {
            CurrentRoot = string.Empty,
            FilesScanned = scannedFiles,
            DirsScanned = scannedDirs,
            CandidatesFound = (ulong)candidates.Count,
            Percent = 100.0,
            Finished = true,
        });

        return new ScanResult
        {
            Candidates = candidates,
            Summary = new ScanSummary
            {
                ScannedDirs = scannedDirs,
                ScannedFiles = scannedFiles,
                ElapsedMs = started.ElapsedMilliseconds,
                TotalPotential = totalPotential,
                TotalCandidates = candidates.Count,
                Categories = categories,
            },
        };
    }

    private static string GetSystemDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/";
        }

        var windows = Environment.GetEnvironmentVariable("WINDIR")
            ?? Path.GetDirectoryName(Environment.SystemDirectory);
        return string.IsNullOrWhiteSpace(windows) ? @"C:\" : Path.GetPathRoot(windows) ?? @"C:\";
    }

    private static readonly object ProgressLock = new();

    private static (List<Candidate> Candidates, ulong Files, ulong Dirs) ScanOneRoot(
        string root,
        ScanOptions options,
        int rootIndex,
        int totalRoots,
        Action<ScanProgress>? onProgress,
        CancellationToken ct)
    {
        var candidates = new List<Candidate>();
        ulong files = 0;
        ulong dirs = 0;

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var current = stack.Pop();
            // Stream the directory listing instead of allocating arrays for the
            // entire directory (huge temp/cache dirs with 100k+ entries used to
            // stall and spike GC).
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    dirs++;
                    if (!ShouldPrune(sub))
                    {
                        stack.Push(sub);
                    }
                }
            }
            catch
            {
                // unreadable directory — skip
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    files++;
                    var candidate = ProcessFile(file, options);
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }

                    if (files % ProgressEveryFiles == 0)
                    {
                        var partial = (files % ProgressWindowFiles) / (double)ProgressWindowFiles;
                        var progress = new ScanProgress
                        {
                            CurrentRoot = root,
                            FilesScanned = files,
                            DirsScanned = dirs,
                            CandidatesFound = (ulong)candidates.Count,
                            Percent = ((rootIndex + partial) / totalRoots) * 100.0,
                            Finished = false,
                        };
                        // Parallel.For invokes this from multiple threads; serialize
                        // the callback so the UI never observes interleaved state.
                        if (onProgress is not null)
                        {
                            lock (ProgressLock)
                            {
                                onProgress(progress);
                            }
                        }
                    }
                }
            }
            catch
            {
                // unreadable directory — skip
            }
        }

        return (candidates, files, dirs);
    }

    public static Candidate? ProcessFile(string path, ScanOptions options)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        var classified = ClassificationEngine.Classify(path);
        if (classified.Kind != MatchKind.Candidate || classified.Category is null)
        {
            return null;
        }

        var category = classified.Category.Value;
        var risk = category.RiskLevel();

        if (!options.IncludeMedium && risk is RiskLevel.Medium or RiskLevel.Advanced)
        {
            return null;
        }

        if (!options.IncludeAdvanced && risk == RiskLevel.Advanced)
        {
            return null;
        }

        DateTimeOffset? accessed = null;
        DateTimeOffset? modified = null;
        try
        {
            accessed = info.LastAccessTimeUtc;
            modified = info.LastWriteTimeUtc;
        }
        catch
        {
            // timestamps may be unavailable for some virtual files
        }

        var locked = classified.BaseConfidence >= 80 && FileState.IsLocked(path);
        var systemAttr = FileState.HasSystemAttribute(path);

        var confidence = ConfidenceEngine.Compute(new ConfidenceInput
        {
            Base = classified.BaseConfidence,
            Category = category,
            Size = info.Length,
            LastAccess = accessed,
            RecencyDays = options.RecencyDays,
            Locked = locked,
            SystemAttr = systemAttr,
        });

        if (confidence < options.MinConfidence)
        {
            return null;
        }

        return new Candidate
        {
            Path = path,
            Size = info.Length,
            Category = category,
            Confidence = confidence,
            Action = ConfidenceEngine.ActionFor(confidence, risk),
            Reason = classified.Reason ?? string.Empty,
            LastModified = modified?.ToString("yyyy-MM-dd"),
            LastAccessDays = accessed is { } a ? ConfidenceEngine.ElapsedDays(a) : null,
            RiskLevel = risk,
        };
    }

    private List<Candidate> SpecialCandidates(ScanOptions options, string systemDriveRoot)
    {
        var outList = new List<Candidate>();

        var rb = _recycleBin.Query();
        if (rb is { Size: > 0 } or { Count: > 0 })
        {
            const byte confidence = 99;
            if (confidence >= options.MinConfidence)
            {
                outList.Add(new Candidate
                {
                    Path = "__recycle_bin__",
                    Size = (long)rb.Value.Size,
                    Category = Category.RecycleBin,
                    Confidence = confidence,
                    Action = CandidateAction.Delete,
                    Reason = $"Recycle Bin contains {rb.Value.Count} items",
                    RiskLevel = RiskLevel.Safe,
                });
            }
        }

        var memDump = Path.Combine(systemDriveRoot, "MEMORY.DMP");
        if (File.Exists(memDump))
        {
            var c = ProcessFile(memDump, options);
            if (c is not null)
            {
                c = new Candidate
                {
                    Path = c.Path,
                    Size = c.Size,
                    Category = Category.CrashDump,
                    Confidence = Math.Max(c.Confidence, (byte)95),
                    Action = CandidateAction.Delete,
                    Reason = "System memory dump at drive root",
                    RiskLevel = RiskLevel.Safe,
                };
                outList.Add(c);
            }
        }

        return outList;
    }

    public ScanResult ScanDuplicates(IReadOnlyList<string> roots, CancellationToken ct)
    {
        var sizeMap = new Dictionary<long, List<string>>();

        foreach (var root in roots)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(current))
                    {
                        if (!ShouldPrune(sub))
                        {
                            stack.Push(sub);
                        }
                    }
                }
                catch
                {
                    // unreadable directory — skip
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        try
                        {
                            var length = new FileInfo(file).Length;
                            if (length >= DuplicateMinSize)
                            {
                                if (!sizeMap.TryGetValue(length, out var list))
                                {
                                    list = new List<string>();
                                    sizeMap[length] = list;
                                }

                                list.Add(file);
                            }
                        }
                        catch
                        {
                            // unreadable or removed concurrently — skip
                        }
                    }
                }
                catch
                {
                    // unreadable directory — skip
                }
            }
        }

        var candidates = new List<Candidate>();
        foreach (var group in sizeMap.Values.Where(g => g.Count >= 2))
        {
            var hashes = new Dictionary<byte[], string>(new ByteArrayComparer());
            foreach (var path in group)
            {
                ct.ThrowIfCancellationRequested();
                var hash = HashFile(path);
                if (hash is null)
                {
                    continue;
                }

                if (hashes.TryGetValue(hash, out var first))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        candidates.Add(new Candidate
                        {
                            Path = path,
                            Size = info.Length,
                            Category = Category.DuplicateFiles,
                            Confidence = 98,
                            Action = CandidateAction.Review,
                            Reason = $"Duplicate of {first}",
                            LastModified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd"),
                            LastAccessDays = ConfidenceEngine.ElapsedDays(info.LastAccessTimeUtc),
                            RiskLevel = RiskLevel.Advanced,
                            GroupId = Convert.ToHexString(hash),
                        });
                    }
                    catch
                    {
                        // removed concurrently
                    }
                }
                else
                {
                    hashes[hash] = path;
                }
            }
        }

        candidates.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        return new ScanResult
        {
            Candidates = candidates,
            Summary = new ScanSummary
            {
                TotalPotential = candidates.Sum(c => c.Size),
                TotalCandidates = candidates.Count,
            },
        };
    }

    /// <summary>Streams a file through a BLAKE3 hasher. Returns null when unreadable.</summary>
    public static byte[]? HashFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var hasher = Blake3.Hasher.New();
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Update(buffer.AsSpan(0, read));
            }

            return hasher.Finalize().AsSpan().ToArray();
        }
        catch
        {
            return null;
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) =>
            x is not null && y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
