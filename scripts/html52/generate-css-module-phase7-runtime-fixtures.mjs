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
    ['phase7-runtime-dynamic-timelines.css', createDynamicTimelinesCss()],
    ['phase7-runtime-cssom-houdini.css', createCssomHoudiniCss()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createDynamicTimelinesCss() {
  return [
    '@keyframes phase7-fade {',
    '  from { opacity: 0; transform: translateX(0); offset-distance: 0%; }',
    '  50% { opacity: .65; transform: translateX(12px); offset-distance: 45%; }',
    '  to { opacity: 1; transform: translateX(24px); offset-distance: 100%; }',
    '}',
    '',
    '@scroll-timeline phase7-scroll {',
    '  source: selector(#phase7-scrollport);',
    '  orientation: block;',
    '  scroll-offsets: start 0%, end 100%;',
    '}',
    '',
    '@view-transition {',
    '  navigation: auto;',
    '}',
    '',
    '::view-transition-group(phase7-card) {',
    '  animation-duration: 180ms;',
    '  animation-timing-function: ease-in-out;',
    '}',
    '',
    '.phase7-animation {',
    '  animation: phase7-fade 200ms ease-in-out 50ms 2 alternate both;',
    '  animation-composition: accumulate;',
    '  animation-delay: 50ms;',
    '  animation-direction: alternate;',
    '  animation-duration: 200ms;',
    '  animation-fill-mode: both;',
    '  animation-iteration-count: 2;',
    '  animation-name: phase7-fade;',
    '  animation-play-state: paused;',
    '  animation-range: entry 10% exit 90%;',
    '  animation-timeline: phase7-scroll;',
    '  transition: opacity 150ms linear, transform 250ms steps(4, end);',
    '  transition-behavior: allow-discrete;',
    '  view-transition-name: phase7-card;',
    '  offset-path: path("M 0 0 L 40 20 L 80 0");',
    '  offset-distance: 25%;',
    '  offset-position: normal;',
    '  offset-rotate: auto 45deg;',
    '}',
    '',
    '@supports (animation-timeline: scroll()) {',
    '  .phase7-scroll-linked {',
    '    animation-name: phase7-fade;',
    '    animation-timeline: scroll(root block);',
    '    animation-range-start: entry 0%;',
    '    animation-range-end: exit 100%;',
    '  }',
    '}',
    ''
  ].join('\n');
}

function createCssomHoudiniCss() {
  return [
    '@font-face {',
    '  font-family: Phase7RuntimeFont;',
    '  src: local("Phase7RuntimeFont");',
    '  font-display: swap;',
    '}',
    '',
    '@property --phase7-runtime-length {',
    '  syntax: "<length>";',
    '  inherits: false;',
    '  initial-value: 12px;',
    '}',
    '',
    '@supports (background: paint(phase7-checker)) {',
    '  .phase7-paint-worklet {',
    '    --phase7-runtime-length: 18px;',
    '    background-image: paint(phase7-checker);',
    '    background-size: var(--phase7-runtime-length) var(--phase7-runtime-length);',
    '  }',
    '}',
    '',
    '.phase7-layout-worklet {',
    '  display: layout(phase7-masonry);',
    '  layout-name: phase7-masonry;',
    '  contain: layout paint;',
    '}',
    '',
    '::highlight(phase7-search-match) {',
    '  background-color: rgb(255 230 120 / .72);',
    '  color: CanvasText;',
    '  text-decoration: underline;',
    '}',
    '',
    '.phase7-cssom-query-target {',
    '  width: calc(10px + var(--phase7-runtime-length));',
    '  min-height: max(1lh, 20px);',
    '  scroll-margin-top: 12px;',
    '  scroll-padding-inline: 8px;',
    '  resize: both;',
    '}',
    '',
    '.phase7-spatial-navigation {',
    '  nav-up: auto;',
    '  nav-right: #phase7-next;',
    '  nav-down: auto;',
    '  nav-left: auto;',
    '  spatial-navigation-action: focus;',
    '  spatial-navigation-contain: contain;',
    '}',
    ''
  ].join('\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  });
}
