#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultSuiteRoot,
  normalizePathForDisplay,
  writeText
} from './common.mjs';

const casesRoot = path.join(defaultSuiteRoot, 'generated', 'css-modules', 'cases');
const bundledFontUrl = '../../../../../Source/Broiler.HTML.Image.Compat/Fonts/Vazirmatn-Regular.ttf';

async function main() {
  const fixtures = new Map([
    ['phase5-text-fonts.html', createFontsHtml()],
    ['phase5-text-inline-ruby.html', createInlineRubyHtml()],
    ['phase5-text-writing-bidi.html', createWritingBidiHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createFontsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 5 CSS fonts and feature settings</title>',
    '<style>',
    '@font-face {',
    '  font-family: Phase5Vazir;',
    `  src: url("${bundledFontUrl}");`,
    '  font-weight: 400;',
    '  font-feature-settings: "kern" 1;',
    '}',
    '@font-feature-values Phase5Vazir {',
    '  @styleset { phase5-alt: 1; }',
    '  @swash { phase5-swash: 2; }',
    '}',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px Phase5Vazir, serif; background: #ffffff; }',
    '#phase5-font-root { width: 560px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase5-font-main { font-family: Phase5Vazir, serif; font-size: 22px; line-height: 28px; font-style: normal; font-weight: 400; font-variant: small-caps; font-feature-settings: "liga" 1, "kern" 1; font-variant-alternates: styleset(phase5-alt); font-variation-settings: "wght" 500; font-optical-sizing: auto; width: 360px; min-height: 36px; background: #e7f5ff; }',
    '#phase5-font-fallback { font-family: Phase5Missing, Phase5Vazir, serif; font-size: 18px; line-height: 24px; font-synthesis-weight: auto; font-synthesis-style: auto; width: 340px; min-height: 30px; margin-top: 10px; background: #ebfbee; }',
    '#phase5-font-palette { font-palette: light; font-size-adjust: none; width: 260px; height: 26px; margin-top: 10px; background: #fff3bf; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase5-font-root">',
    '<div id="phase5-font-main">Feature text ABC 123</div>',
    '<div id="phase5-font-fallback">Fallback text DEF 456</div>',
    '<div id="phase5-font-palette">Palette probe</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createInlineRubyHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 5 CSS inline text, decoration, and ruby</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/22px serif; background: #ffffff; }',
    '#phase5-inline-root { width: 620px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase5-inline-flow { width: 330px; padding: 6px; border: 3px solid #1971c2; background: #e7f5ff; white-space: pre-wrap; overflow-wrap: anywhere; word-break: break-word; line-break: anywhere; text-wrap: balance; text-indent: 24px; text-align: start; hanging-punctuation: first; }',
    '#phase5-decoration { display: inline; text-decoration-line: underline overline; text-decoration-style: wavy; text-decoration-color: #c2255c; text-decoration-thickness: 2px; text-underline-offset: 3px; background: #fff0f6; }',
    '#phase5-transform { display: inline-block; margin-top: 10px; text-transform: uppercase; letter-spacing: 1px; word-spacing: 4px; background: #fff9db; }',
    'ruby.phase5-ruby { ruby-position: over; ruby-align: center; background: #f3f0ff; }',
    'ruby.phase5-ruby rt { font-size: 10px; color: #5f3dc4; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase5-inline-root">',
    '<p id="phase5-inline-flow"><span id="phase5-decoration">Decorated inline text wraps across the inline formatting context.</span> Text continues with a verylongunbrokenphase5word for breaking.</p>',
    '<div id="phase5-transform">mixed case text</div>',
    '<p><ruby class="phase5-ruby">&#x6f22;<rp>(</rp><rt>kan</rt><rp>)</rp>&#x5b57;<rp>(</rp><rt>ji</rt><rp>)</rp></ruby> <ruby class="phase5-ruby">HTML<rt>markup</rt></ruby></p>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createWritingBidiHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 5 CSS writing modes and bidi</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase5-writing-root { width: 620px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase5-vertical { writing-mode: vertical-rl; text-orientation: mixed; direction: rtl; unicode-bidi: bidi-override; inline-size: 90px; block-size: 170px; margin-inline-start: 12px; padding-block-start: 6px; border-inline-start: 4px solid #0b7285; background: #e3fafc; }',
    '#phase5-logical { writing-mode: horizontal-tb; direction: rtl; unicode-bidi: plaintext; width: 280px; min-height: 54px; margin-top: 14px; padding-inline-start: 12px; border-inline-end: 4px solid #e8590c; background: #fff4e6; }',
    '#phase5-sideways { writing-mode: sideways-lr; text-orientation: sideways; width: 130px; height: 28px; margin-top: 12px; background: #edf2ff; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase5-writing-root">',
    '<div id="phase5-vertical">Vertical ABC &#x05d0;&#x05d1;&#x05d2;</div>',
    '<div id="phase5-logical">English line<br>&#x05e9;&#x05dc;&#x05d5;&#x05dd; mixed 123<br>Neutral line</div>',
    '<div id="phase5-sideways">Sideways text</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  });
}
