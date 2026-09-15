using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        _recycleBin = recycleBin ?? CreateDefaultRecycleBin();
        _rootsCatalog = rootsCatalog ?? ScanRootsCatalog.Embedded;
    }

    private static IRecycleBin CreateDefaultRecycleBin() =>
        OperatingSystem.IsWindows() ? new WindowsRecycleBin() : new UnixRecycleBin();

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
    /// True when <paramref name="path"/> is a reparse point (Windows symlink, junction, or mount point)
    /// or a Unix symlink. Such entries must never be descended into during a scan: they can point
    /// outside the scan root to an arbitrary directory, which would let unrelated files be
    /// classified as junk and deleted. See the callers for the safety rationale.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return IsWindowsReparsePoint(path);
            }

            // Unix: check for symlink
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch
        {
            // unreadable or removed concurrently — treat as prune-safe
            return true;
        }
    }

    private static bool IsWindowsReparsePoint(string path)
    {
        try
        {
            var handle = CreateFile(
                path,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
            {
                // Cannot open: fail closed (prune) — same as the outer catch.
                return true;
            }

            try
            {
                var buffer = new byte[16 * 1024];
                var result = DeviceIoControl(
                    handle,
                    FSCTL_GET_REPARSE_POINT,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)buffer.Length,
                    out var bytesReturned,
                    IntPtr.Zero);

                if (!result || bytesReturned < 4)
                {
                    // Distinguish "not a reparse point" (normal directory,
                    // must be scanned) from genuine failures (prune).
                    const int ERROR_NOT_A_REPARSE_POINT = 4390;
                    int err = Marshal.GetLastWin32Error();
                    if (!result && err == ERROR_NOT_A_REPARSE_POINT)
                    {
                        return false;
                    }
                    // No reparse data readable: fail closed (prune).
                    return true;
                }

                // REPARSE_DATA_BUFFER starts with the 4-byte ReparseTag.
                // Deny by default: ANY reparse tag prunes the directory.
                // An allowlist of 4 tags missed APPEXECLINK, LX_SYMLINK,
                // OneDrive/ProjFS, DFS and friends — unknown links were
                // descended into, escaping the scan root.
                var tag = BitConverter.ToUInt32(buffer, 0);

                return tag != 0;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch
        {
            // Fail closed: unreadable entries are pruned, never descended.
            return true;
        }
    }

    // P/Invoke definitions for Windows reparse point detection
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint IO_REPARSE_TAG_HSM = 0xC0000004;
    private const uint IO_REPARSE_TAG_HSM2 = 0x80000006;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public ScanResult Scan(ScanOptions options, Action<ScanProgress>? onProgress, CancellationToken ct) =>
        ScanAsync(options, onProgress, ct).GetAwaiter().GetResult();

    public async Task<ScanResult> ScanAsync(ScanOptions options, Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var roots = options.Roots.Count > 0 ? options.Roots.ToList() : DefaultScanRoots(options.IncludeMedium, options.IncludeAdvanced).ToList();
        var totalRoots = Math.Max(1, roots.Count);

        var results = new System.Collections.Concurrent.ConcurrentBag<(List<Candidate> Candidates, ulong Files, ulong Dirs, bool Truncated)>();
        var rootFileCounts = new ulong[roots.Count];
        var rootDirCounts = new ulong[roots.Count];
        var rootCandidateCounts = new int[roots.Count];

        await System.Threading.Tasks.Parallel.ForEachAsync(
            Enumerable.Range(0, roots.Count),
            new System.Threading.Tasks.ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, token) =>
            {
                token.ThrowIfCancellationRequested();
                results.Add(await ScanOneRootAsync(roots[i], options, i, totalRoots, onProgress, token, rootFileCounts, rootDirCounts, rootCandidateCounts));
            });

        ct.ThrowIfCancellationRequested();

        var candidates = results.SelectMany(r => r.Candidates).ToList();
        candidates.AddRange(SpecialCandidates(options, systemDriveRoot: GetSystemDriveRoot()));

        var scannedFiles = results.Aggregate(0ul, static (a, r) => SaturatingAddUlong(a, r.Files));
        var scannedDirs = results.Aggregate(0ul, static (a, r) => SaturatingAddUlong(a, r.Dirs));

        var catStats = new Dictionary<Category, (int Count, long Size, long Potential)>();
        foreach (var c in candidates)
        {
            var entry = catStats.GetValueOrDefault(c.Category);
            entry.Count++;
            entry.Size = SaturatingAdd(entry.Size, c.Size);
            if (c.Action is CandidateAction.Delete or CandidateAction.Review)
            {
                entry.Potential = SaturatingAdd(entry.Potential, c.Size);
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
            .Aggregate(0L, SaturatingAddSize);

        var truncated = results.Any(r => r.Truncated);

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
                Truncated = truncated,
            },
        };
    }

    private static long SaturatingAdd(long a, long b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long SaturatingAddSize(long acc, Candidate c) => SaturatingAdd(acc, c.Size);

    private static ulong SaturatingAddUlong(ulong a, ulong b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
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

    private static async Task<(List<Candidate> Candidates, ulong Files, ulong Dirs, bool Truncated)> ScanOneRootAsync(
        string root,
        ScanOptions options,
        int rootIndex,
        int totalRoots,
        Action<ScanProgress>? onProgress,
        CancellationToken ct,
        ulong[] rootFileCounts,
        ulong[] rootDirCounts,
        int[] rootCandidateCounts)
    {
        var candidates = new List<Candidate>();
        ulong files = 0;
        ulong dirs = 0;
        bool truncated = false;

        // Root itself is trusted (explicitly chosen) but must be absolute,
        // existing and not a reparse point — a symlink root would scan
        // an arbitrary outside tree.
        if (!IsScannableRoot(root))
        {
            return (candidates, files, dirs, truncated);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (current, depth) = stack.Pop();
            string canonical;
            try
            {
                canonical = Path.GetFullPath(current);
            }
            catch
            {
                continue;
            }
            if (!visited.Add(canonical))
            {
                continue; // cycle guard (reparse/tag-gap aliases)
            }

            try
            {
                await foreach (var sub in EnumerateStreamingAsync(current, static p => Directory.EnumerateDirectories(p), ct))
                {
                    dirs++;
                    if (depth + 1 > options.MaxDepth)
                    {
                        truncated = true;
                        continue;
                    }
                    if (!ShouldPrune(sub)
                        && !IsReparsePoint(sub)
                        && !Safety.PathExclusions.IsExcluded(sub, options.Exclusions))
                    {
                        stack.Push((sub, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }

            try
            {
                await foreach (var file in EnumerateStreamingAsync(current, static p => Directory.EnumerateFiles(p), ct))
                {
                    if (Safety.PathExclusions.IsExcluded(file, options.Exclusions))
                    {
                        continue;
                    }

                    var fileCount = Interlocked.Increment(ref files);
                    rootFileCounts[rootIndex] = fileCount;

                    var candidate = ProcessFile(file, options);
                    if (candidate is not null)
                    {
                        if (candidates.Count >= options.MaxCandidates)
                        {
                            truncated = true;
                            continue;
                        }
                        candidates.Add(candidate);
                        rootCandidateCounts[rootIndex] = candidates.Count;
                    }

                    if (fileCount % ProgressEveryFiles == 0)
                    {
                        var partial = (fileCount % ProgressWindowFiles) / (double)ProgressWindowFiles;
                        var progress = new ScanProgress
                        {
                            CurrentRoot = root,
                            FilesScanned = fileCount,
                            DirsScanned = dirs,
                            CandidatesFound = (ulong)candidates.Count,
                            Percent = ((rootIndex + partial) / totalRoots) * 100.0,
                            Finished = false,
                        };
                        if (onProgress is not null)
                        {
                            onProgress(progress);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }

        rootFileCounts[rootIndex] = files;
        rootDirCounts[rootIndex] = dirs;
        rootCandidateCounts[rootIndex] = candidates.Count;

        return (candidates, files, dirs, truncated);
    }

    /// <summary>
    /// A scan root must be absolute, existing and not itself a reparse point.
    /// The root's own contents are trusted (explicitly chosen); descent below
    /// it is still filtered by <see cref="ShouldPrune"/> + reparse checks.
    /// </summary>
    public static bool IsScannableRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            return false;
        try
        {
            if (!Directory.Exists(root) || IsReparsePoint(root))
                return false;
        }
        catch
        {
            return false;
        }
        return true;
    }

    public static Candidate? ProcessFile(string path, ScanOptions options)
    {
        // File reparse points (symlinks) are never classified: hashing or
        // deleting them would act on the link TARGET under a false identity.
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return null;
        }
        catch
        {
            return null;
        }

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
                    Size = rb.Value.Size > (ulong)long.MaxValue ? long.MaxValue : (long)rb.Value.Size,
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
        var exclusionPatterns = exclusions ?? Array.Empty<string>();
        bool truncated = false;

        var hashMap = new System.Collections.Concurrent.ConcurrentDictionary<byte[], string>(new ByteArrayComparer());
        var candidates = new List<Candidate>();

        foreach (var root in roots)
        {
            if (!IsScannableRoot(root))
            {
                continue;
            }
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (current, depth) = stack.Pop();
                string canonical;
                try
                {
                    canonical = Path.GetFullPath(current);
                }
                catch
                {
                    continue;
                }

                try
                {
                    await foreach (var sub in EnumerateStreamingAsync(current, static p => Directory.EnumerateDirectories(p), ct))
                    {
                        if (depth + 1 > MaxDuplicateDepth)
                        {
                            truncated = true;
                            continue;
                        }
                        if (!ShouldPrune(sub)
                            && !IsReparsePoint(sub)
                            && !Safety.PathExclusions.IsExcluded(sub, exclusionPatterns))
                        {
                            stack.Push((sub, depth + 1));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
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
                            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                            var length = new FileInfo(file).Length;
                            if (length < DuplicateMinSize)
                            {
                                continue;
                            }
                            if (length > MaxHashBytes)
                            {
                                // Hashing multi-GB files for duplicate detection
                                // is a DoS vector; skip (safe direction).
                                continue;
                            }

                            var hash = await HashFileAsync(file, ct);
                            if (hash is null)
                            {
                                continue;
                            }

                            if (hashMap.TryGetValue(hash, out var first))
                            {
                                var info = new FileInfo(file);
                                candidates.Add(new Candidate
                                {
                                    Path = file,
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
                            else
                            {
                                hashMap[hash] = file;
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
                }
            }
        }

        candidates.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        return new ScanResult
        {
            Candidates = candidates,
            Summary = new ScanSummary
            {
                TotalPotential = candidates.Aggregate(0L, SaturatingAddSize),
                TotalCandidates = candidates.Count,
                Truncated = truncated,
            },
        };
    }

    /// <summary>Upper bound for a single file hashed during duplicate scan.</summary>
    private const long MaxHashBytes = 512L * 1024 * 1024;

    /// <summary>Descent cap for duplicate scans (no ScanOptions on this path).</summary>
    private const int MaxDuplicateDepth = 64;

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
        // Bounded channel: a million-entry directory must not buffer
        // unboundedly, and the producer must observe cancellation.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1024)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var entry in enumerate(path))
                {
                    await channel.Writer.WriteAsync(entry, ct);
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // unreadable directory — surface to the consumer
                channel.Writer.TryComplete(ex);
            }
        }, ct);

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
