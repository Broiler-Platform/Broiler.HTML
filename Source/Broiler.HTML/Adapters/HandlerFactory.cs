using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Handlers;
using Broiler.HTML.Dom.Core.Dom;

namespace Broiler.HTML.Adapters;


internal sealed class HandlerFactory : IHandlerFactory
{
    public static readonly HandlerFactory Instance = new();

    public Core.Core.ISelectionHandler CreateSelectionHandler(object root) => new SelectionHandler((CssBox)root);
}
