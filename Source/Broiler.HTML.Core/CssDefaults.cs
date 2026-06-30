namespace Broiler.HTML.Core;

internal static class CssDefaults
{
    public const string DefaultStyleSheet = @"
        html, address,
        blockquote,
        body, dd, div,
        dl, dt, fieldset, form,
        frame, frameset,
        h1, h2, h3, h4,
        h5, h6, noframes,
        ol, p, ul, center,
        dir, menu, pre   { display: block }
        li              { display: list-item }
        head            { display: none }
        table           { display: table }
        tr              { display: table-row }
        thead           { display: table-header-group }
        tbody           { display: table-row-group }
        tfoot           { display: table-footer-group }
        col             { display: table-column }
        colgroup        { display: table-column-group }
        td, th          { display: table-cell }
        caption         { display: table-caption }
        th              { font-weight: bolder; text-align: center }
        caption         { text-align: center }
        body            { margin: 8px }
        h1              { font-size: 2em; margin: .67em 0 }
        h2              { font-size: 1.5em; margin: .75em 0 }
        h3              { font-size: 1.17em; margin: .83em 0 }
        h4, p,
        blockquote, ul,
        fieldset, form,
        ol, dl, dir,
        menu            { margin: 1em 0 }
        h5              { font-size: .83em; margin: 1.5em 0 }
        h6              { font-size: .75em; margin: 1.67em 0 }
        h1, h2, h3, h4,
        h5, h6, b,
        strong          { font-weight: bolder; }
        blockquote      { margin-left: 40px; margin-right: 40px }
        i, cite, em,
        var, address    { font-style: italic }
        pre, tt, code,
        kbd, samp       { font-family: monospace }
        pre             { white-space: pre }
        button, textarea,
        input, select   { display: inline-block; border: 1px solid #767676;
                          padding: 1px 2px; background-color: #ffffff;
                          font-size: 13.3333px; font-family: Arial, sans-serif; }
        input            { min-width: 173px; height: 1.2em; }
        input[type=""hidden""] { display: none; min-width: 0; height: auto;
                          border: none; padding: 0; }
        input[type=""checkbox""],
        input[type=""radio""] { min-width: 13px; width: 13px; height: 13px;
                          padding: 0; }
        input[type=""range""] { min-width: 0; height: auto; padding: 0; border: none; }
        input[type=""submit""],
        input[type=""button""],
        input[type=""reset""] { min-width: 0; padding: 1px 6px;
                          background-color: #f0f0f0; text-align: center; }
        select           { min-width: 60px; height: 1.4em; }
        button           { padding: 1px 6px; background-color: #f0f0f0; text-align: center; }
        textarea         { min-width: 170px; min-height: 3em; }
        big             { font-size: 1.17em }
        small, sub, sup { font-size: .83em }
        sub             { vertical-align: sub }
        sup             { vertical-align: super }
        table           { border-spacing: 2px; }
        thead, tbody,
        tfoot, tr       { vertical-align: middle }
        td, th          { vertical-align: inherit; padding: 1px }
        s, strike, del  { text-decoration: line-through }
        hr              { border: 1px inset; }
        ol, ul, dir,
        menu, dd        { margin-left: 40px }
        ol              { list-style-type: decimal }
        ol ul, ul ol,
        ul ul, ol ol    { margin-top: 0; margin-bottom: 0 }
        ol ul, ul ul   { list-style-type: circle }
        ul ul ul, 
        ol ul ul, 
        ul ol ul        { list-style-type: square }
        u, ins          { text-decoration: underline }
        /*br:before       { content: ""\A"" }*/
        br:before       { content: """" }
        :before, :after { white-space: pre-line }
        center          { text-align: center }
        :link, :visited { text-decoration: underline }
        :focus          { outline: thin dotted invert }

        /* Begin bidirectionality settings (do not change) */
        BDO[DIR=""ltr""]  { direction: ltr; unicode-bidi: bidi-override }
        BDO[DIR=""rtl""]  { direction: rtl; unicode-bidi: bidi-override }

        *[DIR=""ltr""]    { direction: ltr; unicode-bidi: embed }
        *[DIR=""rtl""]    { direction: rtl; unicode-bidi: embed }

        @media print {
          h1            { page-break-before: always }
          h1, h2, h3,
          h4, h5, h6    { page-break-after: avoid }
          ul, ol, dl    { page-break-before: avoid }
        }

        /* Not in the specification but necessary */
        a               { color: #0055BB; text-decoration:underline }
        table           { border-color:#dfdfdf; }
        /* NOTE: no blanket `td, th { border-color }` rule — real UA stylesheets
           (e.g. Chromium) set a default border-color on `table` only, not on
           cells. A cell rule here is a UA *longhand* that the post-cascade
           shorthand expansion cannot override, so an author `td{border:1px solid
           green}` shorthand kept its UA grey border-color and rendered grey
           instead of green (every author-bordered table cell — WPT issue #1143
           css/CSS2/tables/border-conflict-*). Legacy `<table border>` cells get
           their grey from DomParser.ApplyTableBorder, not from here. */
        /* Replaced inline elements — WHATWG default rendering */
        iframe          { border: 2px inset; display: inline-block }
        object          { display: inline-block }

        /* HTML5 semantic/sectioning elements – display:block per WHATWG */
        section, article,
        nav, aside,
        header, footer,
        main, figure,
        figcaption,
        details         { display: block }
        summary         { display: list-item; list-style-type: none }

        /* HTML5 text-level elements – inline by default */
        mark            { background-color: yellow; color: black }

        /* Hidden elements (HTML5) */
        template, dialog,
        [hidden]        { display: none }
        style, title,
        script, link,
        meta, area,
        base, param     { display:none }
        hr              { border-top-color: #9A9A9A; border-left-color: #9A9A9A; border-bottom-color: #EEEEEE; border-right-color: #EEEEEE; }
        pre             { font-size: 10pt; margin-top: 15px; }
        
        /*This is the background of the HtmlToolTip*/
        .htmltooltip {
            border:solid 1px #767676;
            background-color:white;
            background-gradient:#E4E5F0;
            padding: 8px; 
            Font: 9pt Tahoma;
        }";
}
