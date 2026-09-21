using System;
using Broiler.HTML.Core.Utils;
using Broiler.Layout.Engine;

namespace Broiler.HTML.Dom.Parse;

/// <summary>
/// HTML §4.2.3: the first <c>&lt;base&gt;</c> element that carries an <c>href</c> gives the
/// document its base URL, and every relative URL in the document — a stylesheet
/// <c>href</c>, an image <c>src</c>, a <c>url()</c> inside an inline sheet — resolves against
/// it, including the references that appear before the <c>&lt;base&gt;</c> in source order.
/// The embedder's document URL is only the fallback, for a document that sets no base.
/// </summary>
internal static class DocumentBaseUrl
{
    /// <summary>
    /// Finds the document base URL in the tree rooted at <paramref name="box"/>, resolved
    /// against <paramref name="fallbackBaseUrl"/>, and returns that fallback when the document
    /// declares no usable base.
    /// </summary>
    public static Uri Resolve(CssBox box, Uri fallbackBaseUrl)
    {
        if (box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("base", StringComparison.OrdinalIgnoreCase) &&
            box.HtmlTag.TryGetAttribute("href") is { } href &&
            CommonUtils.TryResolveUri(href, fallbackBaseUrl, out var baseUrl))
        {
            return baseUrl;
        }

        foreach (var child in box.Boxes)
        {
            var childBase = Resolve(child, fallbackBaseUrl);
            if (!childBase.Equals(fallbackBaseUrl))
                return childBase;
        }

        return fallbackBaseUrl;
    }

    /// <summary>
    /// Resolves the document base URL and stamps it onto every box in the tree, so that the
    /// boxes built while the base was still unknown — an <c>&lt;img&gt;</c> that precedes the
    /// <c>&lt;base&gt;</c> is the ordinary case — load their sub-resources against it. Returns
    /// the resolved base for the rest of the tree build to hand to the boxes it creates.
    /// </summary>
    public static Uri Apply(CssBox root, Uri fallbackBaseUrl)
    {
        var documentBaseUrl = Resolve(root, fallbackBaseUrl);
        if (!documentBaseUrl.Equals(fallbackBaseUrl))
            Rebase(root, documentBaseUrl);

        return documentBaseUrl;
    }

    private static void Rebase(CssBox box, Uri documentBaseUrl)
    {
        box.BaseUrl = documentBaseUrl;

        foreach (var child in box.Boxes)
            Rebase(child, documentBaseUrl);
    }
}
