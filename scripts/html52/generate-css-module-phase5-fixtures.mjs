#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultSuiteRoot,
  normalizePathForDisplay,
  writeText
} from './common.mjs';

const generatedRoot = path.join(defaultSuiteRoot, 'generated', 'css-modules');
const casesRoot = path.join(generatedRoot, 'cases');

async function main() {
  const fixtures = new Map([
    ['stress-declaration-matrix.css', createDeclarationMatrixCss()],
    ['stress-selector-media-matrix.css', createSelectorMediaMatrixCss()],
    ['stress-calc-gradient-layout.css', createCalcGradientLayoutCss()],
    ['stress-deep-rules.css', createDeepRulesCss()],
    ['resource-isolation.html', createResourceIsolationHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createDeclarationMatrixCss() {
  const cascadeValues = [
    ['color', '#123456'],
    ['color', 'rgb(10 20 30 / 0.8)'],
    ['margin', '1px 2px 3px 4px'],
    ['margin-inline-start', 'calc(var(--phase5-gap) * 2)'],
    ['padding', '2px 4px'],
    ['border', '1px solid currentColor'],
    ['border-block-start-width', '3px'],
    ['font', 'italic small-caps 600 14px/1.4 serif'],
    ['font-size', 'clamp(12px, 2vw, 18px)'],
    ['list-style', 'inside decimal-leading-zero'],
    ['transition', 'color 120ms ease-in-out, margin 1s steps(4, end)'],
    ['background', 'linear-gradient(45deg, red, blue) no-repeat border-box'],
    ['background-color', 'color-mix(in srgb, #123456 40%, white)'],
    ['outline', '2px solid Highlight'],
    ['box-shadow', '0 0 2px #000, inset 0 0 3px #fff']
  ];
  const longhands = [
    ['border-top-left-radius', '6px 2px'],
    ['border-top-right-radius', '4px'],
    ['border-bottom-right-radius', '8px'],
    ['border-bottom-left-radius', '10px'],
    ['text-decoration-line', 'underline overline'],
    ['text-decoration-style', 'wavy'],
    ['text-decoration-thickness', 'from-font'],
    ['text-underline-offset', '0.2em']
  ];

  const lines = [
    ':root {',
    '  --phase5-gap: 8px;',
    '  --phase5-loop-a: var(--phase5-loop-b);',
    '  --phase5-loop-b: var(--phase5-loop-a);',
    '  --phase5-fallback-color: #13579b;',
    '}',
    '',
    '@layer reset, theme, overrides;',
    '',
    '@layer reset {',
    '  .phase5-declaration {',
    '    all: unset;',
    '    display: block;',
    '  }',
    '}',
    '',
    '@layer theme {',
    '  .phase5-declaration {',
    ...cascadeValues.map(([property, value]) => `    ${property}: ${value};`),
    '  }',
    '}',
    '',
    '@layer overrides {',
    '  .phase5-declaration[data-state="active"] {',
    '    color: var(--phase5-loop-a, var(--phase5-fallback-color)) !important;',
    '    width: calc(100% - var(--phase5-missing-length));',
    '    height: calc(10px + 5deg);',
    '    background-color: rgb(300 -10 20 / 120%);',
    ...longhands.map(([property, value]) => `    ${property}: ${value};`),
    '  }',
    '}',
    '',
    '@supports (color: lab(50% 20 30)) {',
    '  .phase5-declaration {',
    '    color: lab(50% 20 30);',
    '    accent-color: color(display-p3 0.2 0.5 0.7);',
    '  }',
    '}',
    '',
    '.phase5-inheritance {',
    '  font-family: serif;',
    '  line-height: 1.5;',
    '  color: inherit;',
    '  border-color: currentColor;',
    '  margin: revert-layer;',
    '}',
    ''
  ];

  return lines.join('\n');
}

function createSelectorMediaMatrixCss() {
  const selectorList = Array.from({ length: 48 }, (_, index) => `.phase5-list-${String(index).padStart(2, '0')}`);
  const mediaQueries = [
    'screen and (min-width: 320px)',
    '(width >= 40rem)',
    '(prefers-color-scheme: dark)',
    '(update: fast) and (hover: hover)',
    'print and (resolution >= 150dpi)'
  ];

  const lines = [
    `${selectorList.join(',\n')} {`,
    '  color: #224466;',
    '  background-color: #f6f8fa;',
    '}',
    '',
    '#phase5-root .item.item[data-state~="active"]:not(.disabled)::before {',
    '  content: "phase5";',
    '  display: inline;',
    '  color: rebeccapurple;',
    '}',
    '',
    'section:has(> .phase5-card),',
    '.phase5-card:is(article, aside, nav) > :where(h2, p) {',
    '  border-inline-start: 4px solid currentColor;',
    '  padding-inline-start: 1rem;',
    '}',
    '',
    ...mediaQueries.flatMap((query, index) => [
      `@media ${query} {`,
      `  .phase5-media-${index} {`,
      `    order: ${index};`,
      `    grid-template-columns: repeat(${index + 1}, minmax(0, 1fr));`,
      `    color: rgb(${30 + index * 20} ${50 + index * 12} ${80 + index * 8});`,
      '  }',
      '}',
      ''
    ]),
    '@supports selector(:has(*)) {',
    '  main:has(.phase5-card:nth-child(2n + 1)) {',
    '    outline: 1px solid currentColor;',
    '  }',
    '}',
    '',
    '@container phase5-size (inline-size > 20rem) {',
    '  .phase5-container-child {',
    '    display: grid;',
    '    gap: 1rem;',
    '  }',
    '}',
    ''
  ];

  return lines.join('\n');
}

function createCalcGradientLayoutCss() {
  const layoutRules = Array.from({ length: 16 }, (_, index) => [
    `.phase5-layout-${String(index).padStart(2, '0')} {`,
    `  min-width: min(${index + 4}rem, 50%);`,
    `  max-width: max(${index + 12}ch, calc(100% - ${index + 1}rem));`,
    `  width: clamp(${index + 2}rem, ${10 + index}vw, ${index + 20}rem);`,
    `  min-height: calc(${index + 1}em + ${index * 2}px);`,
    `  max-height: fit-content(${index + 10}rem);`,
    `  inset: ${index}px auto auto ${index + 1}px;`,
    '  position: relative;',
    '}',
    ''
  ].join('\n'));

  const gradients = [
    'linear-gradient(90deg, #102030 0%, #f0f6ff 100%)',
    'radial-gradient(circle at 20% 30%, rgba(0, 128, 255, 0.6), transparent 40%)',
    'conic-gradient(from 45deg, red, yellow, lime, cyan, blue, magenta, red)',
    'repeating-linear-gradient(45deg, #000 0 2px, #fff 2px 4px)',
    'repeating-radial-gradient(circle, #345 0 1px, #abc 1px 3px)'
  ];

  const lines = [
    ...layoutRules,
    '.phase5-paint-order {',
    `  background-image: ${gradients.join(', ')};`,
    '  background-blend-mode: multiply, screen, overlay, normal, normal;',
    '  box-shadow: 0 0 2px #000, 0 4px 8px rgb(0 0 0 / 0.25), inset 0 0 6px #fff;',
    '  filter: drop-shadow(0 1px 2px #123456) blur(0.2px);',
    '  mix-blend-mode: multiply;',
    '}',
    '',
    '.phase5-invalid-calc {',
    '  width: calc(100% - );',
    '  min-width: calc((((10px)));',
    '  transform: translate(calc(10px + ));',
    '  background-image: linear-gradient(to right, red, );',
    '}',
    ''
  ];

  return lines.join('\n');
}

function createDeepRulesCss() {
  const chainVariables = Array.from({ length: 64 }, (_, index) => (
    index === 63
      ? `  --phase5-chain-${index}: #2468ac;`
      : `  --phase5-chain-${index}: var(--phase5-chain-${index + 1});`
  ));
  const deepSelector = Array.from({ length: 36 }, (_, index) => `.phase5-deep-${String(index).padStart(2, '0')}`).join(' ');
  const selectorList = Array.from({ length: 96 }, (_, index) => `.phase5-wide-${String(index).padStart(2, '0')}`).join(',\n');
  const nestedBlocks = Array.from({ length: 12 }, (_, index) => [
    `@media screen and (min-width: ${320 + index * 40}px) {`,
    `  .phase5-nested-${String(index).padStart(2, '0')} {`,
    `    padding: ${index + 1}px;`,
    `    color: rgb(${20 + index * 10} ${40 + index * 5} ${80 + index * 3});`,
    '  }',
    '}',
    ''
  ].join('\n'));

  const lines = [
    ':root {',
    ...chainVariables,
    '}',
    '',
    `${deepSelector} {`,
    '  color: var(--phase5-chain-0);',
    '  background-color: #eef5ff;',
    '  outline: 1px solid currentColor;',
    '}',
    '',
    `${selectorList} {`,
    '  margin: 0;',
    '  padding: 0;',
    '  border: 0;',
    '}',
    '',
    ...nestedBlocks,
    '.phase5-extreme-layout {',
    '  min-width: 0;',
    '  max-width: 999999px;',
    '  min-height: 0;',
    '  max-height: 999999px;',
    '  overflow: auto;',
    '  contain: layout paint style;',
    '}',
    ''
  ];

  return lines.join('\n');
}

function createResourceIsolationHtml() {
  const svgData = 'data:image/svg+xml,%3Csvg%20xmlns=%22http://www.w3.org/2000/svg%22%20width=%2216%22%20height=%2216%22%3E%3Crect%20width=%2216%22%20height=%2216%22%20fill=%22%2300a36c%22/%3E%3C/svg%3E';
  const altSvgData = 'data:image/svg+xml,%3Csvg%20xmlns=%22http://www.w3.org/2000/svg%22%20width=%228%22%20height=%228%22%3E%3Ccircle%20cx=%224%22%20cy=%224%22%20r=%224%22%20fill=%22%232b6cb0%22/%3E%3C/svg%3E';

  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '  <meta charset="utf-8">',
    '  <title>Phase 5 resource isolation</title>',
    '  <link rel="stylesheet" href="https://example.invalid/phase5.css">',
    '  <link rel="preload" as="font" href="https://example.invalid/phase5.woff2" type="font/woff2">',
    `  <link rel="icon" href="${svgData}">`,
    '  <style>',
    '    @import url("https://example.invalid/imported.css");',
    '    @font-face {',
    '      font-family: phase5;',
    '      src: url("https://example.invalid/font.woff2") format("woff2"), url("data:font/woff2;base64,AAAA") format("woff2");',
    '    }',
    '    .phase5-cursor { cursor: url("https://example.invalid/cursor.cur"), auto; }',
    '  </style>',
    '</head>',
    '<body>',
    '  <main class="phase5-cursor">',
    `    <img alt="data image" src="${svgData}">`,
    '    <img alt="remote image" src="https://example.invalid/image.png">',
    '    <picture>',
    '      <source srcset="https://example.invalid/one.png 1x, ../../../assets/svg/blue-rect.svg 2x" type="image/png">',
    '      <img alt="local image" src="../../../assets/svg/green-square.svg">',
    '    </picture>',
    '    <video poster="https://example.invalid/poster.png" src="data:video/mp4,phase5"></video>',
    '    <audio src="https://example.invalid/audio.mp3"></audio>',
    '    <track src="https://example.invalid/captions.vtt" kind="captions">',
    '    <object data="https://example.invalid/object.svg"></object>',
    `    <embed src="${altSvgData}">`,
    '    <iframe src="https://example.invalid/frame.html"></iframe>',
    '    <input type="image" alt="submit" src="https://example.invalid/submit.png">',
    '    <script src="https://example.invalid/script.js"></script>',
    '  </main>',
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
