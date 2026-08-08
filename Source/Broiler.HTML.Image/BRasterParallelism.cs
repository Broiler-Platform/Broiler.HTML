using System;
using System.Threading.Tasks;

namespace Broiler.HTML.Image;

/// <summary>
/// The thread budget the managed rasterizer spends on scanline bands, and the partitioner its
/// primitives go through. Multithreading roadmap item #4 (and, when the two rasterizers are
/// unified, item #3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why bands, and why inside the primitive.</b> Every fill in <see cref="BCanvas"/> is a
/// <c>for y { for x { BlendPixel } }</c> over a rectangle whose rows write disjoint pixels and read
/// only state that is fixed for the duration of the call — the clip list, the transform, the layer
/// stack and the source bitmap are all settled before the loop starts and none of them is touched
/// until it ends. Splitting the <c>y</c> range is therefore the whole change: no locks, no
/// reordering, and no arithmetic that depends on which rows a thread happens to own.
/// </para>
/// <para>
/// <b>Identical output at any thread count is the point.</b> Each row computes its pixels from the
/// input geometry alone, so a row's result does not depend on whether another row has run yet. The
/// exit gate — a single-threaded setting reproducing the parallel image exactly — is checkable by
/// comparing bytes, which is what <c>BCanvasBandParallelismTests</c> does.
/// </para>
/// <para>
/// <b>The area threshold is not a micro-optimisation.</b> A page of text is thousands of glyph
/// fills a few hundred pixels each; handing those to the scheduler would cost more than drawing
/// them. Only a fill large enough to amortise the dispatch is split, so the common small primitive
/// takes exactly the code path it took before — the same loop on the same thread.
/// </para>
/// <para>
/// <b>Budget, and who else is spending it.</b> The default is one thread per core, overridable
/// with <c>BROILER_RASTER_THREADS</c>. A host that already runs several renders at once — the WPT
/// runner's worker pool, the CLI's batch processes — should set it down accordingly; N processes
/// each spawning N raster threads is N² threads competing for N cores, which is slower than either
/// alone.
/// </para>
/// </remarks>
internal static class BRasterParallelism
{
    /// <summary>Environment variable that overrides the default thread budget.</summary>
    internal const string ThreadsEnvironmentVariable = "BROILER_RASTER_THREADS";

    /// <summary>Environment variable overriding <see cref="MinimumParallelArea"/>.</summary>
    internal const string MinimumAreaEnvironmentVariable = "BROILER_RASTER_MIN_AREA";

    /// <summary>Environment variable overriding <see cref="MinimumBandArea"/>.</summary>
    internal const string MinimumBandAreaEnvironmentVariable = "BROILER_RASTER_MIN_BAND";

    /// <summary>
    /// Pixels a fill must cover before it is split at all, and pixels a single band must be worth.
    /// </summary>
    /// <remarks>
    /// <b>These are measured, not guessed.</b> The corpus render is thousands of fills of a couple
    /// of thousand pixels each and almost none of a hundred thousand, so where the threshold sits
    /// decides whether band parallelism does anything at all on a page — a conservative value makes
    /// the feature inert rather than merely cautious. They are settable so the sweep that picked
    /// them can be re-run when the rasterizer's per-pixel cost changes; see the raster-scaling mode
    /// of <c>Broiler.Render.Stage.Benchmarks</c>.
    /// </remarks>
    internal static int MinimumParallelArea { get; set; } = ReadConfiguredArea(MinimumAreaEnvironmentVariable, 2048);

    /// <inheritdoc cref="MinimumParallelArea"/>
    internal static int MinimumBandArea { get; set; } = ReadConfiguredArea(MinimumBandAreaEnvironmentVariable, 1024);

    private static int ReadConfiguredArea(string variable, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out var area) && area > 0
            ? area
            : fallback;
    }

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    [ThreadStatic] private static long _inlineFills;
    [ThreadStatic] private static long _splitFills;
    [ThreadStatic] private static long _inlineArea;
    [ThreadStatic] private static long _splitArea;

    /// <summary>
    /// Whether to count what the partitioner decided. Off by default and read once per fill, so the
    /// hot path pays a predictable branch and nothing else.
    /// </summary>
    /// <remarks>
    /// Exists because the interesting question about band parallelism is not how fast a split fill
    /// is — that is arithmetic — but <em>how much of a real page's raster is in fills big enough to
    /// split at all</em>. Without this the answer is a guess, and a scaling table that shows no
    /// speedup cannot distinguish "the threads did not help" from "no fill ever reached the
    /// threshold". Those call for opposite next steps, which is exactly why the counter is here.
    /// </remarks>
    internal static bool CollectDiagnostics { get; set; }

    /// <summary>
    /// Fills taken inline, fills split into bands, and the pixel area of each, counted on the
    /// calling thread since the last reset.
    /// </summary>
    /// <remarks>
    /// <b>Per thread, which is also what makes the counters usable.</b> The decision is taken on
    /// the thread that issues the fill — the bands themselves never count — so a thread's totals
    /// describe exactly the renders that thread drove. Process-wide counters would need
    /// interlocked writes on a path that runs thousands of times per render, and would still be
    /// wrong for anyone measuring one render while another test or worker renders alongside it.
    /// </remarks>
    internal static (long InlineFills, long SplitFills, long InlineArea, long SplitArea) Diagnostics =>
        (_inlineFills, _splitFills, _inlineArea, _splitArea);

    /// <summary>Zeroes the calling thread's counters.</summary>
    internal static void ResetDiagnostics()
    {
        _inlineFills = 0;
        _splitFills = 0;
        _inlineArea = 0;
        _splitArea = 0;
    }

    /// <summary>
    /// Maximum threads a single fill may use. <c>1</c> is the sequential rasterizer — not an
    /// approximation of it, the same loop with one band.
    /// </summary>
    internal static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    private static int ReadConfiguredDegree()
    {
        var configured = Environment.GetEnvironmentVariable(ThreadsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured, out var threads) &&
            threads > 0)
        {
            return threads;
        }

        return Environment.ProcessorCount;
    }

    /// <summary>
    /// Runs <paramref name="band"/> over contiguous, inclusive row bands covering
    /// <c>[minY, maxY]</c> — in parallel when the fill is large enough and the target tolerates
    /// concurrent pixel writes, inline on the calling thread otherwise.
    /// </summary>
    /// <param name="rowWidth">
    /// Pixels in one row of the fill, used to decide whether there is enough work to split. Callers
    /// pass the clipped width, not the primitive's nominal one, so a wide rectangle that is mostly
    /// off-surface is judged on what it will actually draw.
    /// </param>
    /// <param name="concurrentWritesAllowed">
    /// Whether the destination can take pixel writes from several threads. False forces the inline
    /// path, so a target that mirrors into a platform bitmap keeps its single-threaded contract.
    /// </param>
    internal static void ForEachBand(int minY, int maxY, int rowWidth, bool concurrentWritesAllowed, Action<int, int> band)
    {
        ArgumentNullException.ThrowIfNull(band);

        var rows = maxY - minY + 1;
        if (rows <= 0)
            return;

        var threads = BandCount(rows, rowWidth, concurrentWritesAllowed);
        if (CollectDiagnostics)
        {
            var area = (long)rows * Math.Max(0, rowWidth);
            if (threads <= 1)
            {
                _inlineFills++;
                _inlineArea += area;
            }
            else
            {
                _splitFills++;
                _splitArea += area;
            }
        }

        if (threads <= 1)
        {
            band(minY, maxY);
            return;
        }

        var rowsPerBand = (rows + threads - 1) / threads;
        Parallel.For(
            0,
            threads,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            i =>
            {
                var from = minY + (i * rowsPerBand);
                var to = Math.Min(from + rowsPerBand - 1, maxY);
                if (from <= to)
                    band(from, to);
            });
    }

    /// <summary>How many bands this fill is worth, which is <c>1</c> whenever it should stay inline.</summary>
    private static int BandCount(int rows, int rowWidth, bool concurrentWritesAllowed)
    {
        if (!concurrentWritesAllowed || _maxDegreeOfParallelism <= 1 || rowWidth <= 0)
            return 1;

        var area = (long)rows * rowWidth;
        if (area < MinimumParallelArea)
            return 1;

        var affordable = (int)Math.Min(int.MaxValue, area / Math.Max(1, MinimumBandArea));
        return Math.Max(1, Math.Min(Math.Min(_maxDegreeOfParallelism, rows), affordable));
    }
}
