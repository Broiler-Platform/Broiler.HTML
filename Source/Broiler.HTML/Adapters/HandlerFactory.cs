using Broiler.HTML.Core;
using Broiler.HTML.Dom;

namespace Broiler.HTML.Adapters;


internal sealed class HandlerFactory : IHandlerFactory
{
    public static readonly HandlerFactory Instance = new();

    public Core.Core.ISelectionHandler CreateSelectionHandler(object root) => new SelectionHandler((CssBox)root);
}
