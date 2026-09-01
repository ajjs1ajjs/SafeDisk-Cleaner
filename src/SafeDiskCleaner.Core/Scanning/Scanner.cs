using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
    private readonly ScanRootsCatalog _rootsCatalog;

    public Scanner(IRecycleBin? recycleBin = null, ScanRootsCatalog? rootsCatalog = null)
    {
        _recycleBin = recycleBin ?? PlatformServices.RecycleBin;
        _rootsCatalog = rootsCatalog ?? ScanRootsCatalog.Embedded;
    }

    /// <summary>
    /// Default roots from the embedded declarative catalog
    /// (see Rules/scan-roots.json). Prefer <see cref="ResolveScanRoots"/> with a
    /// loaded catalog when host overrides are in play.
    /// </summary>
    public static IReadOnlyList<string> DefaultScanRoots(bool includeMedium, bool includeAdvanced) =>
        ScanRootsCatalog.Embedded.Resolve(includeMedium, includeAdvanced);

    public IReadOnlyList<string> ResolveScanRoots(bool includeMedium, bool includeAdvanced) =>
        _rootsCatalog.Resolve(includeMedium, includeAdvanced);

    public static bool ShouldPrune(string directory) =>
        PathProtection.IsProtectedPath(directory);

    /// <summary>
    /// True when <paramref name="path"/> is a reparse point (Windows junction /
    /// mount point / symlink) or a Unix symlink. Such entries must never be
    /// descended into during a scan: they can point outside the scan root to an
    /// arbitrary directory, which would let unrelated files be classified as
    /// junk and deleted. See the callers for the safety rationale.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch
        {
            // unreadable or removed concurrently — treat as prune-safe
            return true;
        }
    }

    public ScanResult Scan(ScanOptions options, Action<ScanProgress>? onProgress, CancellationToken ct) =>
        ScanAsync(options, onProgress, ct).GetAwaiter().GetResult();

    public async Task<ScanResult> ScanAsync(ScanOptions options, Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var roots = options.Roots.Count > 0 ? options.Roots.ToList() : DefaultScanRoots(options.IncludeMedium, options.IncludeAdvanced).ToList();
        var totalRoots = Math.Max(1, roots.Count);

        var results = new System.Collections.Concurrent.ConcurrentBag<(List<Candidate> Candidates, ulong Files, ulong Dirs)>();

        await System.Threading.Tasks.Parallel.ForEachAsync(
            Enumerable.Range(0, roots.Count),
            new System.Threading.Tasks.ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, token) =>
            {
                token.ThrowIfCancellationRequested();
                results.Add(await ScanOneRootAsync(roots[i], options, i, totalRoots, onProgress, token));
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

    private static async Task<(List<Candidate> Candidates, ulong Files, ulong Dirs)> ScanOneRootAsync(
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
            // stall and spike GC). Async enumeration keeps thread-pool threads
            // free while the OS fills the buffer.
            try
            {
                await foreach (var sub in EnumerateStreamingAsync(current, static p => Directory.EnumerateDirectories(p), ct))
                {
                    dirs++;
                    // Never follow junctions/symlinks: a link inside a scanned root
                    // can point to an arbitrary directory (e.g. an attacker-controlled
                    // %TEMP% junction -> user Documents), which would let a "temp
                    // cache" candidate escape into a real location and be deleted.
                    // Reparse points are treated as hard prunes.
                    if (!ShouldPrune(sub)
                        && !IsReparsePoint(sub)
                        && !Safety.PathExclusions.IsExcluded(sub, options.Exclusions))
                    {
                        stack.Push(sub);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // unreadable directory — skip
            }

            try
            {
                await foreach (var file in EnumerateStreamingAsync(current, static p => Directory.EnumerateFiles(p), ct))
                {
                    if (Safety.PathExclusions.IsExcluded(file, options.Exclusions))
                    {
                        continue;
                    }

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
                        // Parallel.ForEachAsync invokes this from multiple threads;
                        // serialize the callback so the UI never observes interleaved state.
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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

    public ScanResult ScanDuplicates(IReadOnlyList<string> roots, CancellationToken ct) =>
        ScanDuplicatesAsync(roots, ct).GetAwaiter().GetResult();

    public async Task<ScanResult> ScanDuplicatesAsync(
        IReadOnlyList<string> roots,
        CancellationToken ct,
        IReadOnlyList<string>? exclusions = null)
    {
        var sizeMap = new Dictionary<long, List<string>>();
        var exclusionPatterns = exclusions ?? Array.Empty<string>();

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
                    await foreach (var sub in EnumerateStreamingAsync(current, static p => Directory.EnumerateDirectories(p), ct))
                    {
                        if (!ShouldPrune(sub)
                            && !IsReparsePoint(sub)
                            && !Safety.PathExclusions.IsExcluded(sub, exclusionPatterns))
                        {
                            stack.Push(sub);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // unreadable directory — skip
                }

                try
                {
                    await foreach (var file in EnumerateStreamingAsync(current, static p => Directory.EnumerateFiles(p), ct))
                        {
                            if (Safety.PathExclusions.IsExcluded(file, exclusionPatterns))
                            {
                                continue;
                            }

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
                catch (Exception ex) when (ex is not OperationCanceledException)
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
                var hash = await HashFileAsync(path, ct);
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
    public static byte[]? HashFile(string path) => HashFileAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Async variant of <see cref="HashFile"/> using true async file I/O.</summary>
    public static async Task<byte[]?> HashFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);
            using var hasher = Blake3.Hasher.New();
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                hasher.Update(buffer.AsSpan(0, read));
            }

            return hasher.Finalize().AsSpan().ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Streams a blocking <see cref="Directory"/> enumeration through a channel so
    /// callers can consume it with await foreach. The .NET BCL has no native async
    /// directory enumeration yet; this keeps the consuming context responsive,
    /// streams lazily (no arrays), and honors cancellation per entry.
    /// </summary>
    private static async IAsyncEnumerable<string> EnumerateStreamingAsync(
        string path,
        Func<string, IEnumerable<string>> enumerate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true,
        });

        var producer = Task.Run(() =>
        {
            try
            {
                foreach (var entry in enumerate(path))
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    channel.Writer.TryWrite(entry);
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // unreadable directory — surface to the consumer
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        await foreach (var entry in channel.Reader.ReadAllAsync(ct))
        {
            yield return entry;
        }

        await producer;
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
