using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Utils.Core.Utils;

namespace Broiler.HTML.Dom.Core.Dom;

internal sealed class CssLineBox
{
    public CssLineBox(CssBox ownerBox)
    {
        Rectangles = [];
        RelatedBoxes = [];
        Words = [];
        OwnerBox = ownerBox;
        OwnerBox.LineBoxes.Add(this);
    }

    public List<CssBox> RelatedBoxes { get; }
    public List<CssRect> Words { get; }
    public CssBox OwnerBox { get; }
    public Dictionary<CssBox, RectangleF> Rectangles { get; }

    public double LineBottom
    {
        get
        {
            double bottom = 0;

            foreach (var rect in Rectangles)
                bottom = Math.Max(bottom, rect.Value.Bottom);

            return bottom;
        }
    }

    internal void ReportExistanceOf(CssRect word)
    {
        if (!Words.Contains(word))
            Words.Add(word);

        if (!RelatedBoxes.Contains(word.OwnerBox))
            RelatedBoxes.Add(word.OwnerBox);
    }

    internal List<CssRect> WordsOf(CssBox box)
    {
        List<CssRect> r = [];

        foreach (CssRect word in Words)
            if (word.OwnerBox.Equals(box))
                r.Add(word);

        return r;
    }

    internal void UpdateRectangle(CssBox box, double x, double y, double r, double b)
    {
        double leftspacing = box.ActualBorderLeftWidth + box.ActualPaddingLeft;
        double rightspacing = box.ActualBorderRightWidth + box.ActualPaddingRight;
        double topspacing = box.ActualBorderTopWidth + box.ActualPaddingTop;
        double bottomspacing = box.ActualBorderBottomWidth + box.ActualPaddingTop;

        if ((box.FirstHostingLineBox != null && box.FirstHostingLineBox.Equals(this)) || box.IsImage)
            x -= leftspacing;

        if ((box.LastHostingLineBox != null && box.LastHostingLineBox.Equals(this)) || box.IsImage)
            r += rightspacing;

        if (!box.IsImage)
        {
            y -= topspacing;
            b += bottomspacing;
        }

        if (!Rectangles.TryGetValue(box, out RectangleF f))
        {
            Rectangles.Add(box, RectangleF.FromLTRB((float)x, (float)y, (float)r, (float)b));
        }
        else
        {
            Rectangles[box] = RectangleF.FromLTRB(
                (float)Math.Min(f.X, x), (float)Math.Min(f.Y, y),
                (float)Math.Max(f.Right, r), (float)Math.Max(f.Bottom, b));
        }

        if (box.ParentBox != null && box.ParentBox.IsInline)
            UpdateRectangle(box.ParentBox, x, y, r, b);
    }

    internal void AssignRectanglesToBoxes()
    {
        foreach (CssBox b in Rectangles.Keys)
            b.Rectangles.Add(this, Rectangles[b]);
    }

    internal void SetBaseLine(RGraphics g, CssBox b, double baseline)
    {
        //TODO: Aqui me quede, checar poniendo "by the" con un font-size de 3em
        List<CssRect> ws = WordsOf(b);

        if (!Rectangles.TryGetValue(b, out RectangleF r))
            return;

        // CSS 2.1 §10.8.1: For inline-block boxes, vertical-align adjusts
        // the position of the entire atomic box.  Move the box's rectangle
        // and its Location/ActualBottom directly.
        if (b.Display == CssConstants.InlineBlock)
        {
            bool usesDefaultBaseline = string.IsNullOrEmpty(b.VerticalAlign)
                || b.VerticalAlign == CssConstants.Baseline;
            if (usesDefaultBaseline)
                return;

            if (Math.Abs(baseline - r.Top) > 0.01)
            {
                Rectangles[b] = new RectangleF(r.X, (float)baseline, r.Width, r.Height);
                b.Location = new PointF(b.Location.X, (float)baseline);
                b.ActualBottom = baseline + r.Height;
            }
            return;
        }

        //Save top of words related to the top of rectangle
        double gap = 0f;

        if (ws.Count > 0)
        {
            gap = ws[0].Top - r.Top;
        }
        else
        {
            CssRect firstw = CssBoxHelper.FirstWordOccourence(b, this);

            if (firstw != null)
                gap = firstw.Top - r.Top;
        }

        // The `baseline` parameter is the desired word.Top (visual text
        // top coordinate) already computed by ApplyVerticalAlignment.
        double newtop = baseline;

        if (b.ParentBox != null && b.ParentBox.Rectangles.ContainsKey(this) && r.Height < b.ParentBox.Rectangles[this].Height)
        {
            //Do this only if rectangle is shorter than parent's
            double recttop = newtop - gap;
            RectangleF newr = new(r.X, (float)recttop, r.Width, r.Height);
            
            Rectangles[b] = newr;
            b.OffsetRectangle(this, gap);
        }

        foreach (var word in ws)
        {
            if (!word.IsImage)
                word.Top = newtop;
        }
    }

    public override string ToString()
    {
        string[] ws = new string[Words.Count];

        for (int i = 0; i < ws.Length; i++)
            ws[i] = Words[i].Text;

        return string.Join(" ", ws);
    }
}
