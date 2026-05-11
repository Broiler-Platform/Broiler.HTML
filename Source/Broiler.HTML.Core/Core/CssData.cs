using Broiler.HTML.Core.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.HTML.Core.Core;

public sealed class CssData
{
    private static readonly List<CssBlock> _emptyArray = [];
    private readonly Dictionary<string, Dictionary<string, List<CssBlock>>> _mediaBlocks = new(StringComparer.InvariantCultureIgnoreCase);

    internal CssData() => _mediaBlocks.Add("all", new Dictionary<string, List<CssBlock>>(StringComparer.InvariantCultureIgnoreCase));

    /// <summary>Parsed @font-face rules from the stylesheet.</summary>
    public List<CssFontFace> FontFaces { get; } = new();

    internal IDictionary<string, Dictionary<string, List<CssBlock>>> MediaBlocks => _mediaBlocks;

    public bool ContainsCssBlock(string className, string media = "all") => _mediaBlocks.TryGetValue(media, out Dictionary<string, List<CssBlock>> mid) && mid.ContainsKey(className);

    public IEnumerable<CssBlock> GetCssBlock(string className, string media = "all")
    {
        List<CssBlock> block = null;

        if (_mediaBlocks.TryGetValue(media, out Dictionary<string, List<CssBlock>> mid))
            mid.TryGetValue(className, out block);

        return block == null
            ? _emptyArray
            : block.OrderBy(static x => x.Specificity).ThenBy(static x => x.SourceOrder);
    }

    public void AddCssBlock(string media, CssBlock cssBlock)
    {
        if (!_mediaBlocks.TryGetValue(media, out Dictionary<string, List<CssBlock>> mid))
        {
            mid = new Dictionary<string, List<CssBlock>>(StringComparer.InvariantCultureIgnoreCase);
            _mediaBlocks.Add(media, mid);
        }

        if (!mid.TryGetValue(cssBlock.Class, out List<CssBlock> list))
        {
            var list2 = new List<CssBlock> { cssBlock };
            mid[cssBlock.Class] = list2;
        }
        else
        {
            bool merged = false;
            foreach (var block in list)
            {
                // CSS2.1 §6.4.1: Do not merge blocks from different
                // cascade origins (UA vs author) so that per-property
                // origin tracking is preserved.
                if (!block.EqualsSelector(cssBlock) || block.IsUserAgent != cssBlock.IsUserAgent)
                    continue;

                merged = true;
                block.Merge(cssBlock);
                break;
            }

            if (!merged)
            {
                // General blocks (no ancestor/sibling selectors, no
                // attribute conditions, and no pseudo-class) are low
                // specificity — insert first.  Blocks with selectors,
                // attribute conditions, or pseudo-classes have higher
                // specificity and must come later so they override
                // general rules.
                bool isGeneral = cssBlock.Selectors == null
                    && (cssBlock.AttributeConditions == null || cssBlock.AttributeConditions.Count == 0)
                    && cssBlock.PseudoClass == null;
                if (isGeneral)
                    list.Insert(0, cssBlock);
                else
                    list.Add(cssBlock);
            }
        }
    }

    public void Combine(CssData other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // for each media block
        foreach (var mediaBlock in other.MediaBlocks)
        {
            // for each css class in the media block
            foreach (var bla in mediaBlock.Value)
            {
                // for each css block of the css class
                foreach (var cssBlock in bla.Value)
                {
                    // combine with this
                    AddCssBlock(mediaBlock.Key, cssBlock);
                }
            }
        }

        FontFaces.AddRange(other.FontFaces);
    }

    public CssData Clone()
    {
        var clone = new CssData();
        foreach (var mid in _mediaBlocks)
        {
            var cloneMid = new Dictionary<string, List<CssBlock>>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var blocks in mid.Value)
            {
                var cloneList = new List<CssBlock>();
                foreach (var cssBlock in blocks.Value)
                {
                    cloneList.Add(cssBlock.Clone());
                }
                cloneMid[blocks.Key] = cloneList;
            }
            clone._mediaBlocks[mid.Key] = cloneMid;
        }
        clone.FontFaces.AddRange(FontFaces);

        return clone;
    }

    /// <summary>
    /// Marks every block in this <see cref="CssData"/> as originating from
    /// the user-agent default stylesheet.  CSS2.1 §6.4.1: Author-origin
    /// declarations override UA declarations regardless of specificity.
    /// </summary>
    internal void MarkAllBlocksAsUserAgent()
    {
        foreach (var mid in _mediaBlocks.Values)
            foreach (var blocks in mid.Values)
                foreach (var block in blocks)
                    block.IsUserAgent = true;
    }
}
