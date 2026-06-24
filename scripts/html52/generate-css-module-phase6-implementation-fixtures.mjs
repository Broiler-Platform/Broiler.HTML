#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultSuiteRoot,
  normalizePathForDisplay,
  writeText
} from './common.mjs';

const casesRoot = path.join(defaultSuiteRoot, 'generated', 'css-modules', 'cases');

async function main() {
  const fixtures = new Map([
    ['phase6-paint-background-color.html', createBackgroundColorHtml()],
    ['phase6-paint-images-masks-shapes.html', createImagesMasksShapesHtml()],
    ['phase6-paint-effects-transforms.html', createEffectsTransformsHtml()],
    ['phase6-paint-pseudo-shadow-gaps.html', createPseudoShadowGapsHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createBackgroundColorHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 6 CSS background, color, and border paint</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; color: #212529; color-scheme: light; forced-color-adjust: none; print-color-adjust: exact; }',
    '#phase6-color-root { width: 700px; margin: 8px; padding: 8px; border: 2px solid #212529; background: #f8f9fa; }',
    '.phase6-paint-row { display: flex; gap: 12px; align-items: flex-start; }',
    '#phase6-layered-background { width: 180px; height: 86px; padding: 12px; border: 8px double #1864ab; border-radius: 20px 8px 24px 12px; background-color: #d0ebff; background-image: linear-gradient(to right, rgba(255,255,255,.75), rgba(24,100,171,.25)), linear-gradient(to bottom, #4dabf7 0%, #1c7ed6 100%); background-origin: border-box, content-box; background-clip: border-box, content-box; box-decoration-break: clone; }',
    '#phase6-border-area { width: 150px; height: 80px; padding: 10px; border: 10px solid #e67700; border-radius: 16px; background: #ffe8cc; background-clip: border-area; color: #5c2b00; }',
    '#phase6-color-functions { width: 180px; height: 86px; padding: 12px; border: 6px solid currentColor; background: #ebfbee; background: color-mix(in srgb, #2b8a3e 68%, white); color: #2b8a3e; color: lab(42% -32 28); outline: 4px solid #66a80f; }',
    '#phase6-wide-gamut { width: 168px; height: 52px; margin-top: 12px; padding: 8px; border: 5px ridge #862e9c; background: #f3d9fa; background: color(display-p3 0.72 0.35 0.89); color: oklch(45% 0.15 320); dynamic-range-limit: standard; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase6-color-root">',
    '<div class="phase6-paint-row">',
    '<div id="phase6-layered-background">Background layers</div>',
    '<div id="phase6-border-area">Border area</div>',
    '<div id="phase6-color-functions">Color functions</div>',
    '</div>',
    '<div id="phase6-wide-gamut">Wide gamut fallback</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createImagesMasksShapesHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 6 CSS images, masks, and shapes paint</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase6-image-root { width: 700px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase6-gradient-stack { width: 220px; height: 110px; border: 4px solid #0b7285; background-color: #e3fafc; background-image: radial-gradient(circle at 30% 30%, #99e9f2 0%, #15aabf 34%, transparent 36%), linear-gradient(135deg, #e3fafc 0%, #0b7285 100%); background-repeat: no-repeat, repeat; background-size: 96px 96px, 42px 42px; background-position: 12px 8px, 0 0; }',
    '#phase6-conic-image { width: 150px; height: 110px; margin-left: 16px; border: 4px solid #7048e8; background: conic-gradient(from 45deg at 50% 50%, #7048e8, #e64980, #ffd43b, #7048e8); }',
    '#phase6-mask-clip { width: 170px; height: 110px; margin-left: 16px; border: 4px solid #2b8a3e; background: linear-gradient(to bottom right, #b2f2bb, #2f9e44); clip-path: inset(8px round 18px); mask-image: linear-gradient(to right, black 70%, transparent 100%); mask-size: 100% 100%; }',
    '#phase6-image-row { display: flex; align-items: flex-start; }',
    '#phase6-shape-flow { width: 500px; margin-top: 14px; padding: 8px; border: 3px solid #495057; background: #f1f3f5; }',
    '#phase6-shape-float { float: left; width: 82px; height: 82px; margin: 0 12px 6px 0; border-radius: 50%; background: image-set(linear-gradient(#ffa8a8, #c92a2a) 1x); shape-outside: circle(45%); clip-path: circle(45%); }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase6-image-root">',
    '<div id="phase6-image-row"><div id="phase6-gradient-stack"></div><div id="phase6-conic-image"></div><div id="phase6-mask-clip"></div></div>',
    '<div id="phase6-shape-flow"><div id="phase6-shape-float"></div>Shape outside text wraps around the painted circle. This checks image functions, clipping, masks, and shape declarations in one static flow.</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createEffectsTransformsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 6 CSS effects, compositing, and transforms paint</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase6-effects-root { width: 700px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase6-scene { position: relative; width: 440px; height: 180px; isolation: isolate; background: #edf2ff; border: 4px solid #364fc7; overflow: hidden; }',
    '.phase6-layer { position: absolute; width: 150px; height: 80px; padding: 8px; color: #212529; }',
    '#phase6-opacity { left: 18px; top: 22px; opacity: .76; background: #74c0fc; mix-blend-mode: multiply; }',
    '#phase6-transform { left: 124px; top: 42px; background: #ffd43b; transform: translate(18px, -8px) rotate(7deg) scale(1.08); transform-origin: 20px 18px; filter: brightness(1.08) contrast(.92); will-change: transform, opacity; }',
    '#phase6-filter { left: 250px; top: 64px; background: #ff8787; filter: saturate(1.2) opacity(.9); mix-blend-mode: screen; }',
    '#phase6-3d { width: 178px; height: 44px; margin-top: 14px; padding: 8px; border: 4px solid #5c940d; background: #d8f5a2; transform: perspective(240px) rotateX(4deg) translateZ(0); transform-style: preserve-3d; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase6-effects-root">',
    '<div id="phase6-scene"><div id="phase6-opacity" class="phase6-layer">Opacity</div><div id="phase6-transform" class="phase6-layer">Transform</div><div id="phase6-filter" class="phase6-layer">Filter</div></div>',
    '<div id="phase6-3d">3D transform probe</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createPseudoShadowGapsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 6 CSS pseudo, shadow, and gap paint</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase6-shadow-root { width: 700px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase6-shadow-text { width: 300px; min-height: 34px; padding: 8px; color: #862e9c; text-shadow: 3px 3px 0 #eebefa; background: #f8f0fc; border: 3px solid #ae3ec9; }',
    '#phase6-pseudo-box { width: 320px; margin-top: 12px; padding: 8px; border: 3px solid #c2255c; background: #fff0f6; box-shadow: 6px 6px 0 rgba(194,37,92,.32); }',
    '#phase6-pseudo-box::before { content: "before"; display: inline-block; width: 66px; height: 22px; margin-right: 8px; background: #fcc2d7; }',
    '#phase6-pseudo-box::after { content: "after"; display: inline-block; width: 54px; height: 22px; margin-left: 8px; background: #faa2c1; }',
    '#phase6-gap-flex { display: flex; gap: 12px 18px; row-gap: 12px; column-gap: 18px; width: 360px; margin-top: 14px; padding: 8px; border: 3px solid #0b7285; background: #e3fafc; }',
    '.phase6-gap-item { width: 56px; height: 34px; background: #66d9e8; box-shadow: 3px 3px 0 #0b7285; }',
    '#phase6-part-host::part(label) { color: #5c940d; background: #d8f5a2; }',
    '#phase6-list { margin: 12px 0 0 22px; padding: 0; }',
    '#phase6-list li::marker { color: #e67700; font-size: 20px; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase6-shadow-root">',
    '<div id="phase6-shadow-text">Text shadow paint</div>',
    '<div id="phase6-pseudo-box">Pseudo content</div>',
    '<div id="phase6-gap-flex"><div class="phase6-gap-item"></div><div class="phase6-gap-item"></div><div class="phase6-gap-item"></div></div>',
    '<div id="phase6-part-host"><span part="label">Part selector probe</span></div>',
    '<ul id="phase6-list"><li>Marker pseudo</li><li>Second marker</li></ul>',
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
