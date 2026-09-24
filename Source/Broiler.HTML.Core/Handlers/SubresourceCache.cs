using System.Collections.Generic;
using Broiler.Net.Http;

namespace Broiler.HTML.Core.Handlers;

/// <summary>What makes two subresource loads the same request: the URL, the destination and the request's CORS and credentials modes.</summary>
internal readonly record struct SubresourceCacheKey(string Url, RequestDestination Destination, RequestMode Mode, CredentialsMode Credentials);

/// <summary>
/// A container's in-memory cache of subresources loaded through the host's transport, bounded by entry count and total
/// size, least recently used out first. Thread-safe: images load on thread-pool workers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Transport loads bypass the legacy image cache in <c>%TEMP%\HtmlRenderer</c>, which is shared by
/// every process, container and profile and keyed by URL alone, and stylesheets and fonts never had a cache. A host that
/// reparses the same document (every animation step of the Broiler browser does) would otherwise refetch every
/// subresource, with its cookies, each time.
/// </para>
/// <para>
/// <b>Why per container.</b> It is keyed without the document, so it is only valid for one transport and one document
/// context: the container replaces it whenever either changes. The mode is part of the key besides the credentials, so
/// a no-cors response never answers a CORS request for the same URL.
/// </para>
/// </remarks>
internal sealed class SubresourceCache
{
    /// <summary>The most entries kept.</summary>
    public const int MaxEntries = 256;

    /// <summary>The most body bytes kept, across all entries.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>A larger body is served but not kept, so one response cannot evict everything else.</summary>
    public const long MaxEntryBytes = 8L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<SubresourceCacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recency = new();
    private long _bytes;

    /// <summary>The number of entries kept.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>Returns the kept response for <paramref name="key"/> and marks it most recently used.</summary>
    public bool TryGet(SubresourceCacheKey key, out SubresourceResponse response)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                response = node.Value.Response;
                return true;
            }
        }

        response = null!;
        return false;
    }

    /// <summary>Keeps <paramref name="response"/> for <paramref name="key"/>, evicting the least recently used entries past the bounds.</summary>
    public void Add(SubresourceCacheKey key, SubresourceResponse response)
    {
        long size = response.Body.LongLength;
        if (size > MaxEntryBytes)
            return;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recency.Remove(existing);
                _bytes -= existing.Value.Response.Body.LongLength;
                _entries.Remove(key);
            }

            _entries[key] = _recency.AddFirst(new Entry(key, response));
            _bytes += size;

            while (_entries.Count > MaxEntries || _bytes > MaxBytes)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _entries.Remove(last.Value.Key);
                _bytes -= last.Value.Response.Body.LongLength;
            }
        }
    }

    private sealed record Entry(SubresourceCacheKey Key, SubresourceResponse Response);
}
