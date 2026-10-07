using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Broiler.CSS.Dom;
using Broiler.Dom;

namespace Broiler.HTML.Orchestration.Parse;

/// <summary>
/// The renderer's answers for the selectors a page's live state decides: the user-action and element
/// states the page's markup carries, the target of the fragment its host navigated to, and the links its
/// host says the user has visited.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a provider at all.</b> The renderer's matcher reads what a scripting host stamped into the page
/// (<see cref="CssUserActionStateMarkup"/>, <see cref="CssElementStateMarkup"/>) without one, and that is
/// still where those answers come from here. Two answers no stamp can carry need one. A page no scripting
/// host ran has nobody to stamp its target, so its host names the fragment it was navigated to
/// (<see cref="HtmlContainerInt.TargetFragment"/>). And <c>:visited</c> is never in markup: whether a link
/// is visited is the user's history, which only the host has (<see cref="HtmlContainerInt.VisitedLinkPredicate"/>).
/// The style engine paints only colours of it.
/// </para>
/// <para>
/// A target a host stamped wins over the fragment: a page whose script moved its fragment says which
/// element that made the target, and the fragment the host loaded may be older.
/// </para>
/// <para>
/// <b>Asked on several threads at once.</b> The renderer resolves a document's styles on several threads
/// before its box walk (Broiler.Layout's <c>CssStyleRecalc.Warm</c>), and each of them asks whether the
/// links it styles are visited. So the answers are kept in a concurrent map. Two threads that ask about
/// one link both work its answer out, and they agree. A plain dictionary was corrupted by their inserts,
/// and the next lookup threw.
/// </para>
/// </remarks>
internal sealed class RendererSelectorState : ICssSelectorStateProvider
{
    private readonly Func<Uri, bool>? _isVisited;
    private readonly Uri? _baseUrl;
    private readonly DomElement? _fragmentTarget;
    private readonly ConcurrentDictionary<DomElement, bool> _visited = new(ReferenceEqualityComparer.Instance);

    private RendererSelectorState(Func<Uri, bool>? isVisited, Uri? baseUrl, DomElement? fragmentTarget)
    {
        _isVisited = isVisited;
        _baseUrl = baseUrl;
        _fragmentTarget = fragmentTarget;
    }

    /// <summary>
    /// The provider for <paramref name="document"/>, or <see langword="null"/> when the container's host
    /// gave neither a fragment nor a history: the matcher then reads the markup alone, as it always did.
    /// </summary>
    internal static RendererSelectorState? For(HtmlContainerInt container, DomDocument? document, Uri? baseUrl)
    {
        if (container.VisitedLinkPredicate is null && string.IsNullOrEmpty(container.TargetFragment))
            return null;

        return new RendererSelectorState(container.VisitedLinkPredicate, baseUrl, FragmentTargetOf(document, container.TargetFragment));
    }

    public CssUserActionState GetUserActionState(DomElement element) => CssUserActionStateMarkup.Read(element);

    public CssElementState GetElementState(DomElement element)
    {
        var state = CssElementStateMarkup.Read(element);
        if (ReferenceEquals(element, _fragmentTarget))
            state |= CssElementState.Target;
        if (_isVisited is not null && IsVisitedLink(element))
            state |= CssElementState.Visited;
        return state;
    }

    private bool IsVisitedLink(DomElement element)
    {
        if (!element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase) &&
            !element.LocalName.Equals("area", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_visited.TryGetValue(element, out var known))
            return known;

        var href = element.GetAttribute("href") ?? element.GetAttribute("xlink:href");
        Uri? url = null;
        var resolved = href is not null &&
                       (_baseUrl is null ? Uri.TryCreate(href, UriKind.Absolute, out url) : Uri.TryCreate(_baseUrl, href, out url));
        var visited = resolved && url is not null && _isVisited!(url);
        _visited[element] = visited;
        return visited;
    }

    /// <summary>
    /// The element <paramref name="fragment"/> names in <paramref name="document"/> -- HTML's "find a
    /// potential indicated element": the first with that id, else the first <c>a</c> with that name, for
    /// the fragment as written and then percent-decoded -- unless the page's markup stamped a target.
    /// </summary>
    private static DomElement? FragmentTargetOf(DomDocument? document, string? fragment)
    {
        var raw = fragment?.TrimStart('#') ?? string.Empty;
        if (document is null || raw.Length == 0)
            return null;

        var elements = document.Descendants().OfType<DomElement>().ToList();
        if (elements.Any(element => (CssElementStateMarkup.Read(element) & CssElementState.Target) != 0))
            return null;

        return Indicated(raw) ?? Indicated(Decode(raw));

        DomElement? Indicated(string name) =>
            name.Length == 0
                ? null
                : elements.FirstOrDefault(element => element.GetAttribute("id") == name) ??
                  elements.FirstOrDefault(element =>
                      element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase) && element.GetAttribute("name") == name);

        static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }
    }
}
