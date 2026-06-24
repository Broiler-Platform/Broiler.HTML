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
    ['phase4-layout-flex-grid.html', createFlexGridHtml()],
    ['phase4-layout-tables-columns.html', createTablesColumnsHtml()],
    ['phase4-layout-media-paged.html', createMediaPagedHtml()],
    ['phase4-layout-lists-generated.html', createListsGeneratedHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createFlexGridHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 4 flex, grid, and alignment layout</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase4-layout-root { width: 680px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase4-flex { display: flex; flex-direction: row; justify-content: space-between; align-items: center; gap: 8px; width: 360px; height: 74px; padding: 6px; border: 3px solid #1971c2; background: #e7f5ff; }',
    '.phase4-flex-item { width: 62px; height: 30px; margin: 2px; background: #74c0fc; }',
    '.phase4-flex-item:nth-child(2) { align-self: flex-end; width: 76px; height: 34px; }',
    '#phase4-grid { display: grid; grid-template-columns: 72px 86px 64px; grid-template-rows: 32px 38px; justify-items: end; align-items: center; gap: 6px 10px; width: 270px; min-height: 92px; margin-top: 12px; padding: 6px; border: 3px solid #5f3dc4; background: #f3f0ff; }',
    '.phase4-grid-item { width: 42px; height: 24px; background: #b197fc; }',
    '#phase4-grid-a { grid-column: 1; grid-row: 1; justify-self: start; }',
    '#phase4-grid-b { grid-column: 2; grid-row: 1; }',
    '#phase4-grid-c { grid-column: 3; grid-row: 2; align-self: end; }',
    '#phase4-subgrid-probe { display: grid; grid-template-columns: subgrid; grid-column: 1 / span 2; grid-row: 2; width: 118px; height: 24px; background: #d0bfff; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase4-layout-root">',
    '<div id="phase4-flex"><div class="phase4-flex-item"></div><div class="phase4-flex-item"></div><div class="phase4-flex-item"></div></div>',
    '<div id="phase4-grid"><div id="phase4-grid-a" class="phase4-grid-item"></div><div id="phase4-grid-b" class="phase4-grid-item"></div><div id="phase4-grid-c" class="phase4-grid-item"></div><div id="phase4-subgrid-probe"></div></div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createTablesColumnsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 4 tables, multicolumn, regions, and exclusions layout</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase4-table-root { width: 700px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase4-table { display: table; table-layout: fixed; border-collapse: collapse; width: 330px; margin-bottom: 12px; background: #fff9db; }',
    '.phase4-row { display: table-row; }',
    '.phase4-cell { display: table-cell; width: 105px; height: 34px; padding: 5px; border: 3px solid #f08c00; vertical-align: middle; }',
    '.phase4-cell-wide { width: 135px; }',
    '#phase4-columns { column-count: 3; column-gap: 14px; column-rule: 2px solid #0b7285; width: 390px; height: 84px; padding: 6px; border: 3px solid #0b7285; background: #e3fafc; }',
    '#phase4-columns p { margin: 0 0 6px 0; break-inside: avoid; }',
    '#phase4-line-grid { line-grid: create; line-snap: baseline; box-snap: block-start; width: 250px; height: 32px; margin-top: 12px; background: #f8f9fa; }',
    '#phase4-region-source { flow-into: phase4-flow; width: 160px; height: 22px; margin-top: 8px; background: #e9ecef; }',
    '#phase4-region-target { flow-from: phase4-flow; width: 160px; height: 22px; margin-top: 4px; background: #dee2e6; }',
    '#phase4-exclusion { wrap-flow: both; shape-outside: inset(0); width: 130px; height: 24px; margin-top: 8px; background: #ffe3e3; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase4-table-root">',
    '<div id="phase4-table"><div class="phase4-row"><div class="phase4-cell">A</div><div class="phase4-cell phase4-cell-wide">B</div><div class="phase4-cell">C</div></div><div class="phase4-row"><div class="phase4-cell">D</div><div class="phase4-cell phase4-cell-wide">E</div><div class="phase4-cell">F</div></div></div>',
    '<div id="phase4-columns"><p>Alpha beta gamma delta.</p><p>Epsilon zeta eta theta.</p><p>Iota kappa lambda mu.</p></div>',
    '<div id="phase4-line-grid">Line grid probe</div>',
    '<div id="phase4-region-source">Region source</div>',
    '<div id="phase4-region-target">Region target</div>',
    '<div id="phase4-exclusion">Exclusion</div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createMediaPagedHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 4 media, paged, and fragmentation layout</title>',
    '<style>',
    '@page { size: 800px 600px; margin: 24px; @top-left { content: "phase4"; } }',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase4-media-root { width: 620px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase4-media-box { width: 140px; height: 32px; background: #ffe8cc; border: 3px solid #e8590c; }',
    '@media all { #phase4-media-box { margin-left: 12px; } }',
    '@media (min-width: 700px) { #phase4-media-box { width: 180px; } }',
    '@media (width >= 700px) { #phase4-media-range { height: 26px; background: #d3f9d8; } }',
    '#phase4-media-range { width: 150px; height: 20px; margin-top: 10px; background: #ebfbee; border: 2px solid #2f9e44; }',
    '#phase4-fragment { width: 260px; margin-top: 12px; padding: 6px; border: 3px solid #364fc7; break-inside: avoid; page-break-inside: avoid; background: #edf2ff; }',
    '#phase4-fragment h2 { break-before: avoid; page-break-before: auto; margin: 0 0 6px 0; font-size: 18px; line-height: 22px; }',
    '#phase4-fragment p { widows: 2; orphans: 2; margin: 0; }',
    '#phase4-page-float { float-reference: page; float: block-start; clear: both; width: 130px; height: 24px; margin-top: 10px; background: #f3f0ff; border: 2px solid #7048e8; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase4-media-root">',
    '<div id="phase4-media-box"></div>',
    '<div id="phase4-media-range"></div>',
    '<section id="phase4-fragment"><h2>Fragment</h2><p>Stable fragmentation probe text for layout and render output.</p></section>',
    '<div id="phase4-page-float"></div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createListsGeneratedHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 4 lists, counters, and generated content layout</title>',
    '<style>',
    '@counter-style phase4-diamond { system: cyclic; symbols: "*"; suffix: " "; }',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase4-list-root { width: 560px; margin: 8px; padding: 8px; border: 2px solid #212529; counter-reset: phase4-step 2; }',
    '#phase4-list-root::before { content: "Before"; display: block; width: 94px; height: 22px; margin-bottom: 8px; background: #e7f5ff; }',
    '#phase4-list-root::after { content: "After"; display: block; width: 82px; height: 22px; margin-top: 8px; background: #fff3bf; }',
    '#phase4-ordered { list-style: phase4-diamond inside; margin: 0 0 10px 0; padding: 0; width: 230px; border: 2px solid #5c940d; background: #ebfbee; }',
    '#phase4-ordered li { counter-increment: phase4-step; margin: 4px; padding: 2px; }',
    '#phase4-ordered li::before { content: counter(phase4-step) ". "; color: #2b8a3e; }',
    '#phase4-generated { width: 260px; padding: 6px; border: 3px solid #c2255c; background: #fff0f6; string-set: phase4-title content(text); }',
    '#phase4-generated::before { content: "Lead " attr(data-label) " "; display: inline; }',
    '#phase4-generated::after { content: " Tail"; display: inline; }',
    '#phase4-gcpm { running: phase4-running; float: top; width: 150px; height: 24px; margin-top: 10px; background: #f8f9fa; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase4-list-root">',
    '<ol id="phase4-ordered"><li>One</li><li>Two</li><li>Three</li></ol>',
    '<div id="phase4-generated" data-label="attr">Generated content</div>',
    '<div id="phase4-gcpm">Paged generated</div>',
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
