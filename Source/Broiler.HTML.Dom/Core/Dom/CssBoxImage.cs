using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.CSS.Core.Dom;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Drawing;

namespace Broiler.HTML.Dom.Core.Dom;

internal sealed class CssBoxImage : CssBox
{
    private readonly CssRectImage _imageWord;
    private IImageLoadHandler _imageLoadHandler;
    private bool _imageLoadingComplete;

    public CssBoxImage(CssBox parent, HtmlTag tag, Uri baseUrl) : base(parent, tag, baseUrl)
    {
        _imageWord = new CssRectImage(this);
        Words.Add(_imageWord);

    }

    public RImage Image => _imageWord.Image;

    internal override void MeasureWordsSize(RGraphics g)
    {
        if (!_wordsSizeMeasured)
        {
            // Load the CSS background image (if any) via the base class.
            // CssBoxImage overrides MeasureWordsSize entirely, so the base
            // class's background-image loading code would otherwise be skipped.
            LoadBackgroundImageIfNeeded();

            if (_imageLoadHandler == null && (ContainerInt.AvoidAsyncImagesLoading || ContainerInt.AvoidImagesLateLoading))
            {
                _imageLoadHandler = ContainerInt.CreateImageLoadHandler(OnLoadImageComplete);

                if (Content != null && Content != CssConstants.Normal)
                    _imageLoadHandler.LoadImage(Content, HtmlTag?.Attributes, base.BaseUrl);
                else
                {
                    var src = GetAttribute("src");
                    // <object data="..."> fallback: use 'data' attribute when 'src' is absent
                    if (string.IsNullOrEmpty(src))
                        src = GetAttribute("data");
                    _imageLoadHandler.LoadImage(src, HtmlTag?.Attributes, base.BaseUrl);
                }
            }

            MeasureWordSpacing(g);
            _wordsSizeMeasured = true;
        }

        CssLayoutEngine.MeasureImageSize(_imageWord);
    }

    public override void Dispose()
    {
        _imageLoadHandler?.Dispose();
        base.Dispose();
    }

    private void SetErrorBorder()
    {
        SetAllBorders(CssConstants.Solid, "2px", "#A0A0A0");
        BorderRightColor = BorderBottomColor = "#E3E3E3";
    }

    private void OnLoadImageComplete(RImage image, RectangleF rectangle, bool async)
    {
        _imageWord.Image = image;
        _imageWord.ImageRectangle = rectangle;
        _imageLoadingComplete = true;
        _wordsSizeMeasured = false;

        if (_imageLoadingComplete && image == null)
            SetErrorBorder();

        if (!ContainerInt.AvoidImagesLateLoading || async)
        {
            var width = new CssLength(Width);
            var height = new CssLength(Height);
            var layout = width.Number <= 0 || width.Unit != CssUnit.Pixels || height.Number <= 0 || height.Unit != CssUnit.Pixels;

            ContainerInt.RequestRefresh(layout);
        }
    }
}