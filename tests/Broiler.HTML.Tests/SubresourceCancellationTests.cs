using System.Diagnostics;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using Broiler.Net.Http;

namespace Broiler.HTML.Tests;

/// <summary>
/// A render tree's loads belong to it: tearing the tree down (a reparse, <c>Dispose</c>) cancels the requests it
/// still has in flight, so they stop instead of exchanging cookies for a document nobody renders any more.
/// </summary>
public sealed class SubresourceCancellationTests
{
    // Well inside the images' own five-second budget, so only the teardown can have stopped the request.
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(3);

    [Fact]
    public Task ReparseCancelsTheImageRequestsOfTheTreeItReplaces() =>
        AssertTeardownCancels(container => container.SetHtmlWithStyleSet("<p>next document</p>"));

    [Fact]
    public Task DisposeCancelsTheImageRequestsInFlight() =>
        AssertTeardownCancels(container => container.Dispose());

    [Fact]
    public void ImageThatRunsOutOfTimeIsALoadFailureNotACancellation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LoopbackHttpServer(async (request, stopping) =>
        {
            await release.Task.WaitAsync(stopping);
            return LoopbackResponse.Ok("image/png", TestContent.Png);
        });

        try
        {
            // The session's own timeout, shorter than the image budget, so the test does not wait five seconds.
            using var session = new BrowserNetworkSession(new() { Timeout = TimeSpan.FromMilliseconds(500) });
            var page = server.Url("/page");
            using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

            var errors = TestContent.Render(container, """<img src="/hang.png">""", page);

            // A cancelled image never reports completion; one that failed does, so layout moves on without it.
            Assert.Equal([HtmlRenderErrorType.Image], errors);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static async Task AssertTeardownCancels(Action<HtmlContainer> tearDown)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LoopbackHttpServer(async (request, stopping) =>
        {
            if (request.Path == "/slow.png")
                await release.Task.WaitAsync(stopping);

            return LoopbackResponse.Ok("image/png", TestContent.Png);
        });

        try
        {
            using var session = new BrowserNetworkSession();
            var recording = new RecordingTransport(session);
            var page = server.Url("/page");
            using var container = new HtmlContainer
            {
                // Image loads run on thread-pool workers, so layout returns while this one waits for its response.
                AvoidAsyncImagesLoading = false,
                AvoidImagesLateLoading = true,
                RequestTransport = recording,
                DocumentContext = DocumentRequestContext.CreateTopLevel(new Uri(page)),
            };

            container.SetHtmlWithStyleSet("""<img src="/slow.png">""", null, page);
            container.PerformLayout();
            await server.WhenRequested("/slow.png").WaitAsync(TimeSpan.FromSeconds(10));

            var call = recording.Single("/slow.png");
            Assert.False(call.Token.IsCancellationRequested);

            var elapsed = Stopwatch.StartNew();
            tearDown(container);

            Assert.True(call.Token.IsCancellationRequested);
            var outcome = await call.Outcome.WaitAsync(Promptly);
            Assert.IsAssignableFrom<OperationCanceledException>(outcome);
            Assert.True(elapsed.Elapsed < Promptly, $"The request stopped after {elapsed.Elapsed}.");
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
