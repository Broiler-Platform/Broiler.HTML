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
        h5, h6, listing,
        ol, p, ul, center,
        dir, menu, pre,
        plaintext, xmp   { display: block }
        li              { display: list-item }
        legend          { display: block }
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
        menu, listing,
        plaintext, xmp  { margin: 1em 0 }
        h5              { font-size: .83em; margin: 1.5em 0 }
        h6              { font-size: .75em; margin: 1.67em 0 }
        h1, h2, h3, h4,
        h5, h6, b,
        strong          { font-weight: bolder; }
        blockquote      { margin-left: 40px; margin-right: 40px }
        i, cite, em,
        var, address    { font-style: italic }
        /* HTML §15.3.3 (Flow content) gives listing, plaintext and xmp pre's rendering:
           display: block, margin-block: 1em, font-family: monospace, white-space: pre.
           They join pre's display group above and p's margin group (margin-block written as
           `margin: 1em 0`, as for p, which has the same rule), and pre in the two rules below.
           The HTML Standard tokenizes xmp as RAWTEXT and plaintext as PLAINTEXT (Broiler.Dom.Html
           does so from its tokenizer update on), so their content is literal text whose line
           breaks only survive with white-space: pre. Two known gaps, tracked in docs/roadmap.md.
           The tree builder drops a line feed right after a pre or listing start tag, but
           Broiler.Dom.Html keeps it, so white-space: pre paints it as an empty first line; pre
           already had this, and listing now has it too. And pre's legacy `font-size: 10pt;
           margin-top: 15px` further down stays pre-only, so pre still renders smaller than
           listing, plaintext and xmp, with no bottom margin. */
        listing, plaintext,
        pre, xmp, tt, code,
        kbd, samp       { font-family: monospace }
        listing, plaintext,
        pre, xmp        { white-space: pre }
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
        /* HTML §15.3.10 (Form controls): textarea { white-space: pre-wrap }. Its text content
           is the control's value, so its line breaks and indentation must show. Known gap,
           tracked in docs/roadmap.md: the tree builder drops a line feed right after the
           textarea start tag, but Broiler.Dom.Html keeps it, so content that starts on the
           line after the tag paints an empty first line. */
        textarea         { min-width: 170px; min-height: 3em; white-space: pre-wrap; }
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
        /* NOTE: no blanket `table`/`td`/`th` { border-color } UA rule. Such a
           rule is a *longhand* that the post-cascade `border`-shorthand expansion
           cannot override, so an author `table{border:1px solid green}` (or
           `td{...}`) kept the UA grey border-color and rendered grey instead of
           green — every author-bordered table/cell (WPT issue #1143,
           css/CSS2/tables/border-conflict-*). Browsers default a border-color on
           the *table* only; Broiler's cascade can't honour that without breaking
           the author shorthand, so the legacy `<table border>` grey is applied
           directly in DomParser (TranslateAttributes / ApplyTableBorder), not
           here, and author CSS is left to win normally. */
        /* Replaced inline elements — WHATWG default rendering */
        iframe          { border: 2px inset #EEEEEE; display: inline-block }
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

        /* HTML <dialog> — native UA display + box chrome (open dialogs are block boxes
           with a border, padding and white background; closed are display:none).
           Requires the :not([open]) selector-matcher fix, and — so an author reset such
           as `dialog { border:0; background:transparent }` overrides these equal-
           specificity UA rules — the shorthand-vs-longhand origin-precedence cascade fix
           (author `background` shorthand must beat this UA `background-color` longhand). */
        dialog          { display: block; border: 1px solid black; padding: 1em; background-color: white }
        dialog:not([open]) { display: none }

        /* Hidden elements (HTML5) */
        template,
        [hidden]        { display: none }
        style, title,
        script, link,
        meta, area,
        base, param     { display:none }
        /* HTML §15.3.1 (Hidden elements) hides noembed and noframes everywhere, not only a
           frameset's noframes (which DomParser also hides). The HTML Standard tokenizes both
           as RAWTEXT (Broiler.Dom.Html does so from its tokenizer update on), so a visible box
           would paint their fallback markup as literal text. datalist, from the same list, is
           a suggestion source, not content. basefont and rp from that list are left out on
           purpose: Broiler.Dom.Html does not parse basefont as a void element, so it contains
           its following siblings and hiding it would hide them too; and with no ruby layout
           the rp parentheses are the only separator between an rt and its base text.
           Broiler.CSS.Dom's CssUserAgentDefaults.DisplayValues leaves both out for the same
           reasons; hide them in both sheets together (docs/roadmap.md). */
        datalist,
        noembed,
        noframes        { display: none }
        /* The bevel base, not the bevel: Engine.BorderBevel darkens the top and left of an
           `inset` border, turning this into the #9A9A9A/#EEEEEE pair browsers paint. CSS makes
           the initial border-color `currentColor`, which bevels black-on-black; every engine
           substitutes a light grey, and stating it here is how Broiler does that. */
        hr              { border-color: #EEEEEE; }
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
