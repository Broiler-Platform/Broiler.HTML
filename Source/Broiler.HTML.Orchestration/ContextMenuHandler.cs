using System;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using Broiler.Graphics;
using Broiler.HTML.Adapters;
using Broiler.HTML.Orchestration;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.Layout.Engine;
using Broiler.Graphics.Adapters;

namespace Broiler.HTML;

internal sealed class ContextMenuHandler : IDisposable
{
    private static readonly string _selectAll;
    private static readonly string _copy;
    private static readonly string _copyLink;
    private static readonly string _openLink;
    private static readonly string _copyImageLink;
    private static readonly string _copyImage;
    private static readonly string _saveImage;
    private readonly SelectionHandler _selectionHandler;
    private readonly HtmlContainerInt _htmlContainer;
    private readonly RAdapter _adapter;
    private RContextMenu? _contextMenu;
    private RControl? _parentControl;
    private CssRect? _currentRect;
    private CssBox? _currentLink;

    static ContextMenuHandler()
    {
        if (CultureInfo.CurrentUICulture.Name.StartsWith("fr", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Tout sélectionner";
            _copy = "Copier";
            _copyLink = "Copier l'adresse du lien";
            _openLink = "Ouvrir le lien";
            _copyImageLink = "Copier l'URL de l'image";
            _copyImage = "Copier l'image";
            _saveImage = "Enregistrer l'image sous...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("de", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Alle auswählen";
            _copy = "Kopieren";
            _copyLink = "Link-Adresse kopieren";
            _openLink = "Link öffnen";
            _copyImageLink = "Bild-URL kopieren";
            _copyImage = "Bild kopieren";
            _saveImage = "Bild speichern unter...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("it", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Seleziona tutto";
            _copy = "Copia";
            _copyLink = "Copia indirizzo del link";
            _openLink = "Apri link";
            _copyImageLink = "Copia URL immagine";
            _copyImage = "Copia immagine";
            _saveImage = "Salva immagine con nome...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("es", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Seleccionar todo";
            _copy = "Copiar";
            _copyLink = "Copiar dirección de enlace";
            _openLink = "Abrir enlace";
            _copyImageLink = "Copiar URL de la imagen";
            _copyImage = "Copiar imagen";
            _saveImage = "Guardar imagen como...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Выбрать все";
            _copy = "Копировать";
            _copyLink = "Копировать адрес ссылки";
            _openLink = "Перейти по ссылке";
            _copyImageLink = "Копировать адрес изображения";
            _copyImage = "Копировать изображение";
            _saveImage = "Сохранить изображение как...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("sv", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Välj allt";
            _copy = "Kopiera";
            _copyLink = "Kopiera länkadress";
            _openLink = "Öppna länk";
            _copyImageLink = "Kopiera bildens URL";
            _copyImage = "Kopiera bild";
            _saveImage = "Spara bild som...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("hu", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Összes kiválasztása";
            _copy = "Másolás";
            _copyLink = "Hivatkozás címének másolása";
            _openLink = "Hivatkozás megnyitása";
            _copyImageLink = "Kép URL másolása";
            _copyImage = "Kép másolása";
            _saveImage = "Kép mentése másként...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("cs", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Vybrat vše";
            _copy = "Kopírovat";
            _copyLink = "Kopírovat adresu odkazu";
            _openLink = "Otevřít odkaz";
            _copyImageLink = "Kopírovat URL snímku";
            _copyImage = "Kopírovat snímek";
            _saveImage = "Uložit snímek jako...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("da", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Vælg alt";
            _copy = "Kopiér";
            _copyLink = "Kopier link-adresse";
            _openLink = "Åbn link";
            _copyImageLink = "Kopier billede-URL";
            _copyImage = "Kopier billede";
            _saveImage = "Gem billede som...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("nl", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Alles selecteren";
            _copy = "Kopiëren";
            _copyLink = "Link adres kopiëren";
            _openLink = "Link openen";
            _copyImageLink = "URL Afbeelding kopiëren";
            _copyImage = "Afbeelding kopiëren";
            _saveImage = "Bewaar afbeelding als...";
        }
        else if (CultureInfo.CurrentUICulture.Name.StartsWith("fi", StringComparison.InvariantCultureIgnoreCase))
        {
            _selectAll = "Valitse kaikki";
            _copy = "Kopioi";
            _copyLink = "Kopioi linkin osoite";
            _openLink = "Avaa linkki";
            _copyImageLink = "Kopioi kuvan URL";
            _copyImage = "Kopioi kuva";
            _saveImage = "Tallena kuva nimellä...";
        }
        else
        {
            _selectAll = "Select all";
            _copy = "Copy";
            _copyLink = "Copy link address";
            _openLink = "Open link";
            _copyImageLink = "Copy image URL";
            _copyImage = "Copy image";
            _saveImage = "Save image as...";
        }
    }

    public ContextMenuHandler(SelectionHandler selectionHandler, HtmlContainerInt htmlContainer)
    {
        ArgumentNullException.ThrowIfNull(selectionHandler);
        ArgumentNullException.ThrowIfNull(htmlContainer);

        _selectionHandler = selectionHandler;
        _htmlContainer = htmlContainer;
        _adapter = (RAdapter)htmlContainer.Adapter;
    }

    public void ShowContextMenu(RControl parent, CssRect? rect, CssBox? link)
    {
        try
        {
            DisposeContextMenu();

            _parentControl = parent;
            _currentRect = rect;
            _currentLink = link;
            _contextMenu = _adapter.GetContextMenu();

            if (rect != null)
            {
                if (link != null)
                {
                    var linkExist = !string.IsNullOrEmpty(link.HrefLink);
                    _contextMenu.AddItem(_openLink, linkExist, OnOpenLinkClick);

                    if (_htmlContainer.IsSelectionEnabled)
                        _contextMenu.AddItem(_copyLink, linkExist, OnCopyLinkClick);

                    _contextMenu.AddDivider();
                }

                if (rect.IsImage)
                {
                    _contextMenu.AddItem(_saveImage, rect.Image != null, OnSaveImageClick);
                    if (_htmlContainer.IsSelectionEnabled)
                    {
                        _contextMenu.AddItem(_copyImageLink, !string.IsNullOrEmpty(rect.OwnerBox.GetAttribute("src")), OnCopyImageLinkClick);
                        _contextMenu.AddItem(_copyImage, rect.Image != null, OnCopyImageClick);
                    }

                    _contextMenu.AddDivider();
                }

                if (_htmlContainer.IsSelectionEnabled)
                    _contextMenu.AddItem(_copy, rect.Selected, OnCopyClick);
            }

            if (_htmlContainer.IsSelectionEnabled)
                _contextMenu.AddItem(_selectAll, true, OnSelectAllClick);

            if (_contextMenu.ItemsCount > 0)
            {
                _contextMenu.RemoveLastDivider();
                _contextMenu.Show(parent, parent.MouseLocation);
            }
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to show the context menu", ex);
        }
    }

    public void Dispose() => DisposeContextMenu();


    private void DisposeContextMenu()
    {
        try
        {
            _contextMenu?.Dispose();
            _contextMenu = null;
            _parentControl = null;
            _currentRect = null;
            _currentLink = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HtmlRenderer] ContextMenuHandler.DisposeContextMenu error: {ex.Message}");
        }
    }

    private void OnOpenLinkClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_parentControl != null && _currentLink != null)
                _htmlContainer.HandleLinkClicked(_parentControl, _parentControl.MouseLocation, _currentLink);
        }
        catch (HtmlLinkClickedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to open the link from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnCopyLinkClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_currentLink != null)
                _adapter.SetToClipboard(_currentLink.HrefLink);
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to copy the link from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnSaveImageClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_currentRect != null)
            {
                var imageSrc = _currentRect.OwnerBox.GetAttribute("src");
                _adapter.SaveToFile((BImage)_currentRect.Image, Path.GetFileName(imageSrc) ?? "image", Path.GetExtension(imageSrc) ?? "png");
            }
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to save the image from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnCopyImageLinkClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_currentRect != null)
                _adapter.SetToClipboard(_currentRect.OwnerBox.GetAttribute("src"));
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to copy the image URL from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnCopyImageClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_currentRect != null)
                _adapter.SetToClipboard((BImage)_currentRect.Image);
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to copy the image from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnCopyClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            _selectionHandler.CopySelectedHtml();
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to copy the selection from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }

    private void OnSelectAllClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_parentControl != null)
                _selectionHandler.SelectAll(_parentControl);
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.ContextMenu, "Failed to select all from the context menu", ex);
        }
        finally
        {
            DisposeContextMenu();
        }
    }
}
