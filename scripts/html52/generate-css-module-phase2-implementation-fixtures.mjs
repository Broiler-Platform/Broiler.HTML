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
    ['phase2-computed-cascade-selectors.html', createCascadeSelectorsHtml()],
    ['phase2-computed-values-shorthands.html', createValuesShorthandsHtml()],
    ['phase2-computed-custom-properties.html', createCustomPropertiesHtml()],
    ['phase2-computed-registered-properties.html', createRegisteredPropertiesHtml()]
  ]);

  for (const [fileName, contents] of fixtures) {
    const outputPath = path.join(casesRoot, fileName);
    await writeText(outputPath, contents);
    console.log(`Wrote ${normalizePathForDisplay(outputPath)}.`);
  }
}

function createCascadeSelectorsHtml() {
  return [
    '<!doctype html>',
    '<html lang="en">',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 2 computed cascade and selectors</title>',
    '<style>',
    '@layer phase2-base, phase2-theme;',
    '@layer phase2-base {',
    '  .phase2-target { color: #c92a2a; background-color: #fff5f5; border: 1px solid #c92a2a; }',
    '}',
    '@layer phase2-theme {',
    '  #phase2-scope > .phase2-target:is([data-state="active"], .phase2-alt) { color: #2b8a3e; background-color: #ebfbee; border-color: #2b8a3e; }',
    '}',
    '.phase2-target { color: #c92a2a; background-color: #fff5f5; border: 1px solid #c92a2a; }',
    '#phase2-cascade { color: #2b8a3e; background-color: #ebfbee; border-color: #2b8a3e; margin-left: 3px; }',
    '#phase2-scope > .phase2-target[data-state="active"] { color: #2b8a3e; background-color: #ebfbee; border-color: #2b8a3e; margin-left: 3px; }',
    '#phase2-scope > .phase2-target:is([data-state="active"], .phase2-alt) { padding-left: 7px; }',
    '@media all {',
    '  #phase2-scope .phase2-target { text-align: left; }',
    '  #phase2-scope > .phase2-target:lang(en) { font-weight: bold; }',
    '}',
    '#phase2-scope :where(.phase2-target) { margin-left: 3px; }',
    '.phase2-scoped-child { color: #5c940d; }',
    '@scope (#phase2-scope) {',
    '  .phase2-scoped-child { color: #5c940d; }',
    '}',
    '.phase2-nesting {',
    '  color: #c2255c;',
    '  & > .phase2-nesting-child { color: #862e9c; }',
    '}',
    '.phase2-nesting > .phase2-nesting-child { color: #862e9c; }',
    '</style>',
    '</head>',
    '<body><div id="phase2-scope"><div id="phase2-cascade" class="phase2-target" data-state="active" lang="en"></div><div id="phase2-scoped" class="phase2-scoped-child"></div><div class="phase2-nesting"><div id="phase2-nested" class="phase2-nesting-child"></div></div></div></body>',
    '</html>',
    ''
  ].join('\n');
}

function createValuesShorthandsHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 2 computed values and shorthands</title>',
    '<style>',
    '#phase2-values {',
    '  color: #123456;',
    '  background: #f8f9fa;',
    '  width: 120px;',
    '  height: 24px;',
    '  margin: 4px 8px 12px 16px;',
    '  padding: 1px 2px 3px 4px;',
    '  border: 3px solid #654321;',
    '  border-left-width: 5px;',
    '  border-radius: 6px 8px 10px 12px;',
    '  font: italic small-caps bold 18px/20px serif;',
    '  text-indent: 6px;',
    '  word-spacing: 2px;',
    '  opacity: 0.75;',
    '}',
    '</style>',
    '</head>',
    '<body><div id="phase2-values"></div></body>',
    '</html>',
    ''
  ].join('\n');
}

function createCustomPropertiesHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 2 computed custom properties</title>',
    '<style>',
    ':root {',
    '  --phase2-accent: #7048e8;',
    '  --phase2-background: #f3f0ff;',
    '}',
    '#phase2-custom {',
    '  color: var(--phase2-accent);',
    '  background-color: var(--phase2-background, #ffffff);',
    '  border: 2px solid currentColor;',
    '}',
    '#phase2-inherit { color: inherit; border: 1px solid currentColor; }',
    '</style>',
    '</head>',
    '<body><div id="phase2-custom-parent" style="color: #0b7285;"><div id="phase2-custom"></div><div id="phase2-inherit"></div></div></body>',
    '</html>',
    ''
  ].join('\n');
}

function createRegisteredPropertiesHtml() {
  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Phase 2 computed registered properties</title>',
    '<style>',
    '@property --phase2-registered-color {',
    '  syntax: "<color>";',
    '  inherits: true;',
    '  initial-value: #495057;',
    '}',
    '#phase2-registered {',
    '  --phase2-registered-color: #e67700;',
    '  color: var(--phase2-registered-color);',
    '  background-color: #fff4e6;',
    '  border: 2px solid currentColor;',
    '}',
    '</style>',
    '</head>',
    '<body><div id="phase2-registered"></div></body>',
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
