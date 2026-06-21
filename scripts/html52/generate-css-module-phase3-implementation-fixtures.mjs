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
    ['phase3-layout-core.html', createCoreLayoutHtml()],
    ['phase3-layout-overflow-scroll.html', createOverflowScrollHtml()],
    ['phase3-layout-ui-forms.html', createUiFormsHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createCoreLayoutHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 3 core CSS layout</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase3-root { width: 640px; margin: 8px; padding: 6px; border: 2px solid #212529; contain: layout paint; }',
    '#phase3-block { width: 180px; height: 38px; margin: 10px 18px 12px 14px; padding: 6px 8px; border: 4px solid #1c7ed6; background: #d0ebff; box-sizing: content-box; }',
    '#phase3-inline-row { width: 320px; padding: 4px; border: 2px solid #495057; background: #f1f3f5; }',
    '.phase3-inline { display: inline-block; width: 56px; height: 22px; margin-right: 6px; background: #ffd43b; vertical-align: top; }',
    '#phase3-relative { position: relative; left: 12px; top: 5px; width: 90px; height: 24px; margin: 8px 0; background: #b2f2bb; }',
    '#phase3-positioning { position: relative; width: 220px; height: 72px; margin-top: 14px; border: 3px solid #6741d9; background: #f3f0ff; }',
    '#phase3-absolute { position: absolute; left: 32px; top: 18px; width: 74px; height: 26px; background: #d0bfff; }',
    '#phase3-sizing { width: 50%; min-width: 120px; max-width: 160px; height: 28px; margin-top: 10px; background: #ffe8cc; }',
    '#phase3-logical { margin-left: 14px; padding-top: 5px; border-left: 4px solid #e8590c; inline-size: 124px; block-size: 28px; background: #fff4e6; }',
    '#phase3-anchor { anchor-name: --phase3-anchor; width: 64px; height: 18px; margin-top: 8px; background: #c3fae8; }',
    '#phase3-anchor-target { position-anchor: --phase3-anchor; position-area: bottom; width: 76px; height: 18px; margin-top: 4px; background: #96f2d7; }',
    '#phase3-rhythm { block-step-size: 20px; line-height-step: 20px; width: 110px; height: 20px; margin-top: 6px; background: #e7f5ff; }',
    '#phase3-round { shape-inside: display; border-boundary: display; width: 90px; height: 20px; margin-top: 6px; background: #f8f9fa; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase3-root">',
    '<div id="phase3-block"></div>',
    '<div id="phase3-inline-row"><span class="phase3-inline"></span><span class="phase3-inline"></span><span class="phase3-inline"></span></div>',
    '<div id="phase3-relative"></div>',
    '<div id="phase3-positioning"><div id="phase3-absolute"></div></div>',
    '<div id="phase3-sizing"></div>',
    '<div id="phase3-logical"></div>',
    '<div id="phase3-anchor"></div>',
    '<div id="phase3-anchor-target"></div>',
    '<div id="phase3-rhythm"></div>',
    '<div id="phase3-round"></div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createOverflowScrollHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 3 overflow and scrolling CSS layout</title>',
    '<style>',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase3-overflow-root { width: 420px; margin: 8px; padding: 8px; border: 2px solid #343a40; }',
    '#phase3-overflow-hidden { width: 150px; height: 64px; overflow: hidden; border: 4px solid #0b7285; padding: 5px; background: #e3fafc; }',
    '#phase3-overflow-content { width: 240px; height: 112px; background: #99e9f2; margin: 0; }',
    '#phase3-scrollbox { width: 170px; height: 70px; overflow: auto; margin-top: 12px; padding: 6px; border: 3px solid #5c940d; scroll-snap-type: y mandatory; scroll-padding-top: 10px; scrollbar-color: #5c940d #ebfbee; scrollbar-width: thin; background: #ebfbee; }',
    '.phase3-snap { width: 128px; height: 36px; margin-bottom: 8px; scroll-snap-align: start; scroll-margin-top: 5px; background: #b2f2bb; }',
    '#phase3-clip { width: 140px; height: 44px; overflow: clip; margin-top: 12px; border: 3px solid #c92a2a; background: #fff5f5; }',
    '#phase3-clip-child { width: 210px; height: 80px; background: #ffc9c9; }',
    '#phase3-anchor-stable { overflow-anchor: none; width: 130px; height: 24px; margin-top: 12px; background: #f1f3f5; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase3-overflow-root">',
    '<div id="phase3-overflow-hidden"><div id="phase3-overflow-content"></div></div>',
    '<div id="phase3-scrollbox"><div class="phase3-snap"></div><div class="phase3-snap"></div><div class="phase3-snap"></div></div>',
    '<div id="phase3-clip"><div id="phase3-clip-child"></div></div>',
    '<div id="phase3-anchor-stable"></div>',
    '</div>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

function createUiFormsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 3 UI and form CSS layout</title>',
    '<style>',
    '@viewport { width: device-width; zoom: 1; }',
    'html, body { margin: 0; padding: 0; }',
    'body { font: 16px/20px serif; background: #ffffff; }',
    '#phase3-ui-root { width: 520px; margin: 8px; padding: 8px; border: 2px solid #212529; }',
    '#phase3-viewport-unit { width: 25vw; height: 24px; margin-bottom: 8px; background: #d0ebff; }',
    '#phase3-form-grid { width: 360px; padding: 8px; border: 2px solid #495057; background: #f8f9fa; }',
    '#phase3-form-grid input, #phase3-form-grid button, #phase3-form-grid select, #phase3-form-grid textarea { box-sizing: border-box; appearance: auto; outline: 2px solid #339af0; outline-offset: 2px; accent-color: #2f9e44; caret-color: #c92a2a; cursor: pointer; }',
    '#phase3-text { width: 150px; height: 26px; margin: 4px; }',
    '#phase3-button { width: 92px; height: 28px; margin: 4px; }',
    '#phase3-check { width: 18px; height: 18px; margin: 4px; }',
    '#phase3-select { width: 124px; height: 30px; margin: 4px; }',
    '#phase3-textarea { width: 190px; height: 48px; margin: 4px; resize: none; }',
    '#phase3-fieldset { width: 240px; margin-top: 8px; border: 3px solid #862e9c; padding: 8px; }',
    '#phase3-fieldset legend { padding: 0 4px; }',
    '</style>',
    '</head>',
    '<body>',
    '<div id="phase3-ui-root">',
    '<div id="phase3-viewport-unit"></div>',
    '<form id="phase3-form-grid">',
    '<input id="phase3-text" value="text">',
    '<button id="phase3-button" type="button">Button</button>',
    '<input id="phase3-check" type="checkbox" checked>',
    '<select id="phase3-select"><option>One</option><option>Two</option></select>',
    '<textarea id="phase3-textarea">Area</textarea>',
    '<fieldset id="phase3-fieldset"><legend>Group</legend><input value="inside"></fieldset>',
    '</form>',
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
