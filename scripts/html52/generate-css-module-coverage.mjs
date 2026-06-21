#!/usr/bin/env node
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultCoverageRoot,
  defaultSuiteRoot,
  fileExists,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  writeJson
} from './common.mjs';

const defaultRegistryPath = path.join(defaultSuiteRoot, 'generated', 'css-modules', 'registry.json');
const defaultOutputPath = path.join(defaultCoverageRoot, 'css-modules.json');
const defaultImplementationOutputRoot = path.join(defaultCoverageRoot, 'css-module-implementation');

const phase2StableCoreTests = new Map([
  ['background', ['css-module-phase2-paint-effects-001']],
  ['box', ['css-module-phase2-box-layout-001']],
  ['break', ['css-module-phase2-box-layout-001']],
  ['cascade', ['css-module-phase2-syntax-cascade-001']],
  ['color', ['css-module-phase2-values-color-timing-001']],
  ['compositing', ['css-module-phase2-paint-effects-001']],
  ['conditional', ['css-module-phase2-syntax-cascade-001']],
  ['counter-styles', ['css-module-phase2-lists-counters-001']],
  ['css-cascade-5', ['css-module-phase2-syntax-cascade-001']],
  ['css-color-4', ['css-module-phase2-values-color-timing-001']],
  ['css-color-adjust-1', ['css-module-phase2-paint-effects-001']],
  ['css-conditional-4', ['css-module-phase2-syntax-cascade-001']],
  ['css-contain-1', ['css-module-phase2-box-layout-001']],
  ['css-display-3', ['css-module-phase2-box-layout-001']],
  ['css-grid-2', ['css-module-phase2-box-layout-001']],
  ['css-masking', ['css-module-phase2-paint-effects-001']],
  ['css-snappoints-1', ['css-module-phase2-box-layout-001']],
  ['css-scrollbars-1', ['css-module-phase2-box-layout-001']],
  ['css-timing-1', ['css-module-phase2-values-color-timing-001']],
  ['css-will-change-1', ['css-module-phase2-paint-effects-001']],
  ['css21', ['css-module-phase2-box-layout-001']],
  ['flexbox', ['css-module-phase2-box-layout-001']],
  ['fonts', ['css-module-phase2-text-fonts-writing-001']],
  ['geometry-1', ['css-module-phase2-values-color-timing-001']],
  ['grid-layout', ['css-module-phase2-box-layout-001']],
  ['images', ['css-module-phase2-paint-effects-001']],
  ['mediaqueries', ['css-module-phase2-values-color-timing-001']],
  ['mediaqueries-4', ['css-module-phase2-values-color-timing-001']],
  ['multicol', ['css-module-phase2-box-layout-001']],
  ['namespace', ['css-module-phase2-syntax-cascade-001']],
  ['selectors', ['css-module-phase2-syntax-cascade-001']],
  ['shapes', ['css-module-phase2-paint-effects-001']],
  ['style-attr', ['html52-css-style-attribute-inheritance-001']],
  ['syntax', ['css-module-phase2-syntax-cascade-001']],
  ['text', ['css-module-phase2-text-fonts-writing-001']],
  ['text-decor', ['css-module-phase2-text-fonts-writing-001']],
  ['transforms', ['css-module-phase2-paint-effects-001']],
  ['ui', ['css-module-phase2-paint-effects-001']],
  ['values', ['css-module-phase2-values-color-timing-001']],
  ['variables', ['css-module-phase2-syntax-cascade-001']],
  ['writing-modes', ['css-module-phase2-text-fonts-writing-001']],
  ['writing-modes-4', ['css-module-phase2-text-fonts-writing-001']],
  ['cascade-4', ['css-module-phase2-syntax-cascade-001']]
]);

const phase2ComputedStyleTests = new Map([
  ['namespace', ['css-module-phase2-computed-cascade-selectors-001']],
  ['selectors', ['css-module-phase2-computed-cascade-selectors-001']],
  ['style-attr', ['css-module-phase2-computed-custom-properties-001']],
  ['cascade', ['css-module-phase2-computed-cascade-selectors-001']],
  ['conditional', ['css-module-phase2-computed-cascade-selectors-001']],
  ['values', ['css-module-phase2-computed-values-shorthands-001']],
  ['variables', ['css-module-phase2-computed-custom-properties-001']],
  ['syntax', ['css-module-phase2-computed-cascade-selectors-001']],
  ['geometry-1', ['css-module-phase2-computed-values-shorthands-001']],
  ['cascade-4', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-timing-1', ['css-module-phase2-computed-values-shorthands-001']],
  ['css-conditional-4', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-cascade-5', ['css-module-phase2-computed-cascade-selectors-001']],
  ['selectors4', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-values-4', ['css-module-phase2-computed-values-shorthands-001']],
  ['css-properties-values-api-1', ['css-module-phase2-computed-registered-properties-001']],
  ['css-nesting-1', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-cascade-6', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-conditional-5', ['css-module-phase2-computed-cascade-selectors-001']],
  ['css-values-5', ['css-module-phase2-computed-values-shorthands-001']]
]);

const phase3RendererBreadthTests = new Map([
  ['align', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-cross-module-001']],
  ['selectors4', ['css-module-phase3-text-generated-001', 'css-module-phase3-cross-module-001']],
  ['sizing', ['css-module-phase3-layout-breadth-001']],
  ['lists', ['css-module-phase3-text-generated-001']],
  ['positioning', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-cross-module-001']],
  ['css-fonts-4', ['css-module-phase3-text-generated-001']],
  ['css-logical-1', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-cross-module-001']],
  ['css-values-4', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-paint-effects-001']],
  ['css-contain-2', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-cross-module-001']],
  ['paged-media', ['css-module-phase3-media-ui-001']],
  ['ruby', ['css-module-phase3-text-generated-001']],
  ['css-overflow-3', ['css-module-phase3-layout-breadth-001']],
  ['pseudo-4', ['css-module-phase3-text-generated-001']],
  ['css-images-4', ['css-module-phase3-paint-effects-001']],
  ['css-overflow-4', ['css-module-phase3-layout-breadth-001', 'css-module-phase3-cross-module-001']],
  ['css-text-decor-4', ['css-module-phase3-text-generated-001', 'css-module-phase3-cross-module-001']],
  ['mediaqueries-5', ['css-module-phase3-media-ui-001']],
  ['css-sizing-4', ['css-module-phase3-layout-breadth-001']],
  ['device-adapt', ['css-module-phase3-media-ui-001']],
  ['exclusions', ['css-module-phase3-layout-breadth-001']],
  ['filter', ['css-module-phase3-paint-effects-001', 'css-module-phase3-cross-module-001']],
  ['gcpm', ['css-module-phase3-text-generated-001']],
  ['linegrid', ['css-module-phase3-layout-breadth-001']],
  ['regions', ['css-module-phase3-layout-breadth-001']],
  ['tables', ['css-module-phase3-layout-breadth-001']],
  ['inline', ['css-module-phase3-text-generated-001']],
  ['css-round-display-1', ['css-module-phase3-layout-breadth-001']],
  ['css-ui-4', ['css-module-phase3-media-ui-001']],
  ['css-text-4', ['css-module-phase3-text-generated-001']],
  ['css-rhythm-1', ['css-module-phase3-text-generated-001']],
  ['css-shadow-parts-1', ['css-module-phase3-text-generated-001']],
  ['css-scroll-anchoring-1', ['css-module-phase3-layout-breadth-001']],
  ['css-color-5', ['css-module-phase3-paint-effects-001']],
  ['css-transforms-2', ['css-module-phase3-paint-effects-001']],
  ['css-box-4', ['css-module-phase3-layout-breadth-001']],
  ['content', ['css-module-phase3-text-generated-001']],
  ['css-grid-3', ['css-module-phase3-cross-module-001']],
  ['css-borders-4', ['css-module-phase3-paint-effects-001']]
]);

const phase3LayoutRenderTests = new Map([
  ['box', ['css-module-phase3-layout-core-001']],
  ['css-anchor-position-1', ['css-module-phase3-layout-core-001']],
  ['css-box-4', ['css-module-phase3-layout-core-001']],
  ['css-contain-1', ['css-module-phase3-layout-core-001']],
  ['css-contain-2', ['css-module-phase3-layout-core-001']],
  ['css-contain-3', ['css-module-phase3-layout-core-001']],
  ['css-display-3', ['css-module-phase3-layout-core-001']],
  ['css-display-4', ['css-module-phase3-layout-core-001']],
  ['css-logical-1', ['css-module-phase3-layout-core-001']],
  ['css-position-4', ['css-module-phase3-layout-core-001']],
  ['css-rhythm-1', ['css-module-phase3-layout-core-001']],
  ['css-round-display-1', ['css-module-phase3-layout-core-001']],
  ['css-sizing-4', ['css-module-phase3-layout-core-001']],
  ['css21', ['css-module-phase3-layout-core-001']],
  ['positioning', ['css-module-phase3-layout-core-001']],
  ['sizing', ['css-module-phase3-layout-core-001']],
  ['css-overflow-3', ['css-module-phase3-layout-overflow-scroll-001']],
  ['css-overflow-4', ['css-module-phase3-layout-overflow-scroll-001']],
  ['css-scroll-anchoring-1', ['css-module-phase3-layout-overflow-scroll-001']],
  ['css-scrollbars-1', ['css-module-phase3-layout-overflow-scroll-001']],
  ['css-snappoints-1', ['css-module-phase3-layout-overflow-scroll-001']],
  ['css-ui-4', ['css-module-phase3-layout-ui-forms-001']],
  ['device-adapt', ['css-module-phase3-layout-ui-forms-001']],
  ['ui', ['css-module-phase3-layout-ui-forms-001']]
]);

const phase4DraftEarlyTests = new Map([
  ['css-properties-values-api-1', ['css-module-phase4-draft-properties-values-001']],
  ['css-fonts-5', ['css-module-phase4-draft-values-fonts-001']],
  ['css-nesting-1', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css-cascade-6', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css-conditional-5', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css-contain-3', ['css-module-phase4-draft-layout-position-001']],
  ['css-anchor-position-1', ['css-module-phase4-draft-layout-position-001']],
  ['css-values-5', ['css-module-phase4-draft-values-fonts-001']],
  ['css-color-hdr-1', ['css-module-phase4-early-paint-color-001']],
  ['css-display-4', ['css-module-phase4-draft-layout-position-001']],
  ['css-gaps-1', ['css-module-phase4-draft-layout-position-001']],
  ['css-position-4', ['css-module-phase4-draft-layout-position-001']],
  ['css-scoping-1', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css4-background', ['css-module-phase4-early-paint-color-001']],
  ['page-floats', ['css-module-phase4-early-layout-scroll-001']],
  ['fill-stroke-3', ['css-module-phase4-early-paint-color-001']],
  ['css-break-4', ['css-module-phase4-early-layout-scroll-001']],
  ['css-overscroll-1', ['css-module-phase4-early-layout-scroll-001']],
  ['css-scroll-snap-2', ['css-module-phase4-early-layout-scroll-001']],
  ['css-easing-2', ['css-module-phase4-draft-values-fonts-001']],
  ['css-overflow-5', ['css-module-phase4-early-layout-scroll-001']],
  ['css-multicol-2', ['css-module-phase4-early-layout-scroll-001']],
  ['css-mixins-1', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css-env-1', ['css-module-phase4-draft-values-fonts-001']],
  ['css-anchor-position-2', ['css-module-phase4-draft-layout-position-001']],
  ['selectors-5', ['css-module-phase4-draft-syntax-cascade-001']],
  ['css-image-animation-1', ['css-module-phase4-early-paint-color-001']],
  ['css22', ['css-module-phase4-early-paint-color-001']]
]);

const runtimeBoundaryReasons = new Map([
  ['speech', 'Requires an aural/speech rendering backend rather than Broiler.HTML static visual rendering.'],
  ['css-paint-api-1', 'Requires CSS Painting API worklet execution; static CSS parsing remains a negative/no-crash concern.'],
  ['css-view-transitions-1', 'Requires a live document transition timeline and DOM state changes.'],
  ['animations', 'Requires animation timelines and time sampling beyond static document rendering.'],
  ['web-animations', 'Requires the Web Animations runtime API and timelines.'],
  ['transitions', 'Requires style-change timelines beyond a static initial render.'],
  ['motion-1', 'Requires animation path sampling for moving boxes rather than static layout alone.'],
  ['cssom-view', 'Requires browser viewport, scrolling, and geometry APIs.'],
  ['cssom', 'Requires CSS object model APIs and script-visible mutation/query behavior.'],
  ['css-typed-om-1', 'Requires CSS Typed OM script-visible value objects and mutation/query APIs.'],
  ['css-font-loading-3', 'Requires the CSS Font Loading API; static @font-face resource behavior is covered by font/layout tests.'],
  ['resize-observer-1', 'Requires observer callback delivery from a live layout engine.'],
  ['css-layout-api-1', 'Requires Houdini layout worklet execution.'],
  ['css-animation-worklet-1', 'Requires animation worklet execution.'],
  ['css-nav-1', 'Requires focus navigation behavior and event/runtime integration.'],
  ['css-highlight-api-1', 'Requires custom highlight registry APIs and live range state.'],
  ['scroll-animations-1', 'Requires scroll-linked animation timelines.'],
  ['css-animations-2', 'Requires animation timelines and event behavior beyond static rendering.'],
  ['web-animations-2', 'Requires the Web Animations runtime API and timelines.'],
  ['css-transitions-2', 'Requires style-change timelines beyond a static initial render.'],
  ['css-view-transitions-2', 'Requires a live document transition timeline and DOM state changes.']
]);

const familyRules = [
  ['syntax-and-parsing', /syntax|nesting|mixins|style attributes/i],
  ['cascade-and-selection', /cascade|selectors|namespaces|conditional|scoping|custom properties/i],
  ['values-and-units', /values and units|environment variables|easing|geometry/i],
  ['box-and-layout-core', /css level 2|box model|display|sizing|position|anchor positioning|containment|logical/i],
  ['layout-systems', /flexible box|grid|alignment|multi-column|table|exclusions|regions|template layout/i],
  ['overflow-and-scrolling', /overflow|scroll snap|scrollbars|scroll anchoring|overscroll/i],
  ['text-and-fonts', /font|text|inline|ruby|writing modes|line grid|rhythmic/i],
  ['lists-and-generated-content', /lists|counter styles|generated content/i],
  ['paint-and-visual-effects', /color|background|border|image|mask|transform|filter|compositing|fill and stroke|shape/i],
  ['ui-and-forms', /user interface|viewport|round display|spatial navigation/i],
  ['dynamic-timelines', /animation|transition|view transition|motion path/i],
  ['cssom-and-houdini-apis', /cssom|typed om|paint api|layout api|properties and values api|worklet|resize observer|highlight api/i],
  ['media-and-paged-output', /media queries|paged media|fragmentation|page floats/i],
  ['aural-and-speech', /speech/i]
];

async function main(argv = process.argv.slice(2)) {
  try {
    const options = parseArguments(argv);
    if (options.help) {
      console.log(getHelpText());
      return 0;
    }

    const registry = await readJson(options.registryPath);
    const coverage = createCoverageMap(registry);
    const implementationDocuments = createImplementationDocuments(coverage);

    if (options.check) {
      const coverageCheck = await checkJsonFile(options.outputPath, coverage);
      const implementationCheck = await checkImplementationDocuments(options.implementationOutputRoot, implementationDocuments);
      if (!coverageCheck.upToDate || !implementationCheck.upToDate) {
        for (const error of [...coverageCheck.errors, ...implementationCheck.errors]) {
          console.error(error);
        }
        if (coverageCheck.errors.length === 0 && implementationCheck.errors.length === 0) {
          console.error(`${normalizePathForDisplay(options.outputPath)} is out of date. Run npm run html52:css-coverage.`);
        }
        return 1;
      }
      console.log(`${normalizePathForDisplay(options.outputPath)} is up to date.`);
      console.log(`${normalizePathForDisplay(options.implementationOutputRoot)} is up to date.`);
      return 0;
    }

    await writeJson(options.outputPath, coverage);
    await writeImplementationDocuments(options.implementationOutputRoot, implementationDocuments);
    console.log(`Wrote ${coverage.items.length} CSS module coverage rows to ${normalizePathForDisplay(options.outputPath)}.`);
    console.log(`Wrote ${implementationDocuments.length} CSS module implementation status files to ${normalizePathForDisplay(options.implementationOutputRoot)}.`);
    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

function parseArguments(argv) {
  const options = {
    registryPath: defaultRegistryPath,
    outputPath: defaultOutputPath,
    implementationOutputRoot: defaultImplementationOutputRoot,
    check: false,
    help: false
  };

  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (argument === '--') {
      continue;
    }
    const name = argument.includes('=') ? argument.slice(0, argument.indexOf('=')) : argument;

    switch (name) {
      case '--registry': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.registryPath = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--output':
      case '-o': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.outputPath = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--implementation-output-root': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.implementationOutputRoot = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--check':
        options.check = true;
        break;
      case '-h':
      case '--help':
        options.help = true;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  options.registryPath = path.resolve(options.registryPath);
  options.outputPath = path.resolve(options.outputPath);
  options.implementationOutputRoot = path.resolve(options.implementationOutputRoot);
  return options;
}

function createCoverageMap(registry) {
  if (!Array.isArray(registry.modules)) {
    throw new Error('Registry must contain a modules array.');
  }

  return {
    schemaVersion: 1,
    generatedFrom: {
      registry: 'tests/html52/generated/css-modules/registry.json',
      sourceUrl: registry.generatedFrom?.url ?? null,
      snapshotDate: registry.generatedFrom?.snapshotDate ?? null
    },
    items: registry.modules.map((module) => createCoverageItem(module))
  };
}

function createCoverageItem(module) {
  const runtimeReason = runtimeBoundaryReasons.get(module.id);
  const family = classifyFamily(module);
  const supportLevel = runtimeReason ? 'out-of-scope' : supportLevelFor(module.currentStatus);
  const status = runtimeReason ? 'out-of-scope' : 'planned';
  const tests = [
    ...(phase2StableCoreTests.get(module.id) ?? []),
    ...(phase2ComputedStyleTests.get(module.id) ?? []),
    ...(phase3RendererBreadthTests.get(module.id) ?? []),
    ...(phase3LayoutRenderTests.get(module.id) ?? []),
    ...(phase4DraftEarlyTests.get(module.id) ?? [])
  ];
  const implementation = createImplementationMetadata(module, family, supportLevel, tests, runtimeReason);

  return {
    featureId: `css-module-${module.id}`,
    title: module.title,
    cluster: 'css-modules',
    specSections: [module.url],
    supportLevel,
    status,
    tests,
    moduleId: module.id,
    moduleTitle: module.title,
    moduleFamily: family,
    w3cStatus: module.currentStatus,
    upcomingStatus: module.upcomingStatus,
    editorDraftUrl: module.editorDraftUrl,
    ...implementation,
    reason: runtimeReason ?? reasonFor(supportLevel, module.currentStatus, tests)
  };
}

function createImplementationMetadata(module, family, supportLevel, tests, runtimeReason) {
  const oracleDepth = currentOracleDepthFor(tests, runtimeReason);
  const targetOracleDepth = targetOracleDepthFor(family, supportLevel, runtimeReason);
  const owner = ownerFor(family, runtimeReason);
  const blockedBy = blockedByFor(family, runtimeReason);
  return {
    implementationStatus: implementationStatusFor(oracleDepth, targetOracleDepth, tests, runtimeReason),
    oracleDepth,
    targetOracleDepth,
    nextOracle: nextOracleFor(oracleDepth, targetOracleDepth, family, runtimeReason),
    owner,
    blockedBy,
    scopeDecision: runtimeReason ? 'static-renderer-out-of-scope' : 'static-renderer-in-scope',
    outOfScopeDecision: runtimeReason ? outOfScopeDecisionFor(module, family) : null
  };
}

function currentOracleDepthFor(tests, runtimeReason) {
  if (runtimeReason) {
    return 'out-of-scope';
  }
  if (tests.some(hasPhase3LayoutRenderTest)) {
    return 'render';
  }
  if (tests.some((testId) => testId.includes('phase2-computed'))) {
    return 'computedStyle';
  }
  if (tests.some((testId) => testId.startsWith('html52-') && !testId.includes('resource-log'))) {
    return 'render';
  }
  if (tests.some((testId) => testId.includes('resource-log'))) {
    return 'resourceLog';
  }
  if (tests.length > 0) {
    return 'parse';
  }
  return 'none';
}

function targetOracleDepthFor(family, supportLevel, runtimeReason) {
  if (runtimeReason) {
    return 'out-of-scope';
  }
  if (supportLevel === 'experimental') {
    return 'parse';
  }

  switch (family) {
    case 'syntax-and-parsing':
    case 'cascade-and-selection':
    case 'values-and-units':
    case 'cssom-and-houdini-apis':
      return 'computedStyle';
    case 'paint-and-visual-effects':
    case 'miscellaneous':
      return 'render';
    case 'box-and-layout-core':
    case 'layout-systems':
    case 'overflow-and-scrolling':
    case 'text-and-fonts':
    case 'lists-and-generated-content':
    case 'media-and-paged-output':
    case 'ui-and-forms':
      return 'render';
    default:
      return 'computedStyle';
  }
}

function implementationStatusFor(oracleDepth, targetOracleDepth, tests, runtimeReason) {
  if (runtimeReason) {
    return 'out-of-scope-static-renderer';
  }
  if (tests.length === 0) {
    return 'inventory-only';
  }
  if (oracleDepth === targetOracleDepth) {
    return oracleDepth === 'parse' ? 'parser-target-met' : 'target-oracle-smoke';
  }
  if (oracleDepth === 'render') {
    return 'render-smoke';
  }
  if (oracleDepth === 'resourceLog') {
    return 'resource-smoke';
  }
  if (oracleDepth === 'parse') {
    return 'parser-smoke';
  }
  return 'needs-first-oracle';
}

function nextOracleFor(oracleDepth, targetOracleDepth, family, runtimeReason) {
  if (runtimeReason) {
    return 'scope-decision';
  }
  if (oracleDepth === targetOracleDepth) {
    return 'section-complete-matrix';
  }
  if (oracleDepth === 'parse') {
    if (family === 'paint-and-visual-effects' || family === 'miscellaneous') {
      return 'displayList';
    }
    if ([
      'box-and-layout-core',
      'layout-systems',
      'overflow-and-scrolling',
      'text-and-fonts',
      'lists-and-generated-content',
      'media-and-paged-output',
      'ui-and-forms'
    ].includes(family)) {
      return 'layout';
    }
    return 'computedStyle';
  }
  if ((oracleDepth === 'layout' || oracleDepth === 'displayList') && targetOracleDepth === 'render') {
    return 'render';
  }
  return targetOracleDepth;
}

function ownerFor(family, runtimeReason) {
  if (runtimeReason) {
    return family === 'aural-and-speech' ? 'aural-renderer' : 'browser-runtime';
  }

  switch (family) {
    case 'syntax-and-parsing':
    case 'cascade-and-selection':
    case 'values-and-units':
    case 'cssom-and-houdini-apis':
      return 'css-engine';
    case 'box-and-layout-core':
    case 'layout-systems':
    case 'overflow-and-scrolling':
    case 'text-and-fonts':
    case 'lists-and-generated-content':
    case 'media-and-paged-output':
    case 'ui-and-forms':
      return 'layout-engine';
    case 'paint-and-visual-effects':
    case 'miscellaneous':
      return 'paint-engine';
    default:
      return 'css-engine';
  }
}

function blockedByFor(family, runtimeReason) {
  if (!runtimeReason) {
    return [];
  }
  if (family === 'aural-and-speech') {
    return ['aural-renderer'];
  }
  if (family === 'dynamic-timelines') {
    return ['browser-runtime', 'animation-timeline'];
  }
  if (family === 'cssom-and-houdini-apis') {
    return ['browser-runtime', 'script-visible-css-api'];
  }
  return ['browser-runtime'];
}

function outOfScopeDecisionFor(module, family) {
  if (family === 'aural-and-speech') {
    return 'out-of-scope-unless-aural-renderer-is-added';
  }
  if (module.id.includes('font-loading')) {
    return 'out-of-scope-unless-font-loading-api-is-added';
  }
  return 'out-of-scope-unless-browser-runtime-is-added';
}

function supportLevelFor(status) {
  if (status === 'REC' || status === 'CRD' || status === 'CR') {
    return 'required';
  }
  if (status === 'WD') {
    return 'recommended';
  }
  return 'experimental';
}

function reasonFor(supportLevel, currentStatus, tests) {
  if (tests.some(hasPhase3LayoutRenderTest)) {
    return 'Phase 3 layout/render coverage links this module to executable CSS module layout JSON and render PNG oracles for static renderer behavior.';
  }
  if (tests.some((testId) => testId.includes('phase2-computed'))) {
    return 'Phase 2 computed-style coverage links this module to executable cascade, selector, value, custom property, or registered-property assertions.';
  }
  if (tests.some((testId) => testId.includes('phase4'))) {
    return 'Phase 4 draft and early-draft coverage links this module to executable CSS parser/no-crash sweep cases; keep volatile rendering assertions out of release gates until implementation and interoperability mature.';
  }
  if (tests.some((testId) => testId.includes('phase3'))) {
    return 'Phase 3 renderer-breadth coverage links this module to executable CSS parser sweep cases; add computed-style, layout, display-list, or render oracles as implementation depth grows.';
  }
  if (tests.length > 0) {
    return 'Phase 2 stable-core coverage links this module to executable CSS parser sweep cases; add render/layout oracles in later phases where Broiler.HTML implements the rendering behavior.';
  }
  if (supportLevel === 'required') {
    return `${currentStatus} module; renderer-relevant sections need executable coverage or a narrower out-of-scope row.`;
  }
  if (supportLevel === 'recommended') {
    return 'Working Draft module; track support classification and add coverage for broad or implemented features.';
  }
  return 'Early draft module; keep registry coverage and add parser/no-crash smoke tests before making rendering a release gate.';
}

function classifyFamily(module) {
  const haystack = `${module.id} ${module.title}`;
  const matched = familyRules.find(([, pattern]) => pattern.test(haystack));
  return matched ? matched[0] : 'miscellaneous';
}

function hasPhase3LayoutRenderTest(testId) {
  return testId === 'css-module-phase3-layout-core-001'
    || testId === 'css-module-phase3-layout-overflow-scroll-001'
    || testId === 'css-module-phase3-layout-ui-forms-001';
}

function createImplementationDocuments(coverage) {
  const byFamily = new Map();
  for (const item of coverage.items) {
    const family = item.moduleFamily ?? 'unclassified';
    const items = byFamily.get(family) ?? [];
    items.push(createImplementationEntry(item));
    byFamily.set(family, items);
  }

  return [...byFamily.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([family, items]) => ({
      fileName: `${family}.json`,
      document: {
        schemaVersion: 1,
        generatedFrom: coverage.generatedFrom,
        moduleFamily: family,
        itemCount: items.length,
        items: items.sort((a, b) => a.moduleId.localeCompare(b.moduleId))
      }
    }));
}

function createImplementationEntry(item) {
  return {
    moduleId: item.moduleId,
    moduleTitle: item.moduleTitle,
    featureId: item.featureId,
    supportLevel: item.supportLevel,
    w3cStatus: item.w3cStatus,
    upcomingStatus: item.upcomingStatus,
    implementationStatus: item.implementationStatus,
    oracleDepth: item.oracleDepth,
    targetOracleDepth: item.targetOracleDepth,
    nextOracle: item.nextOracle,
    owner: item.owner,
    blockedBy: item.blockedBy,
    scopeDecision: item.scopeDecision,
    outOfScopeDecision: item.outOfScopeDecision,
    tests: item.tests,
    reason: item.reason
  };
}

async function writeImplementationDocuments(outputRoot, documents) {
  await fs.mkdir(outputRoot, { recursive: true });
  for (const document of documents) {
    await writeJson(path.join(outputRoot, document.fileName), document.document);
  }
}

async function checkJsonFile(filePath, expectedValue) {
  const expected = JSON.stringify(expectedValue, null, 2);
  if (!(await fileExists(filePath))) {
    return {
      upToDate: false,
      errors: [`${normalizePathForDisplay(filePath)} does not exist. Run npm run html52:css-coverage.`]
    };
  }

  const current = JSON.stringify(await readJson(filePath), null, 2);
  return {
    upToDate: current === expected,
    errors: current === expected
      ? []
      : [`${normalizePathForDisplay(filePath)} is out of date. Run npm run html52:css-coverage.`]
  };
}

async function checkImplementationDocuments(outputRoot, documents) {
  const errors = [];
  const expectedFileNames = new Set(documents.map((document) => document.fileName));
  if (!(await fileExists(outputRoot))) {
    return {
      upToDate: false,
      errors: [`${normalizePathForDisplay(outputRoot)} does not exist. Run npm run html52:css-coverage.`]
    };
  }

  const actualFileNames = (await fs.readdir(outputRoot)).filter((fileName) => fileName.endsWith('.json'));
  for (const fileName of actualFileNames) {
    if (!expectedFileNames.has(fileName)) {
      errors.push(`${normalizePathForDisplay(path.join(outputRoot, fileName))} is stale. Remove it or regenerate CSS coverage.`);
    }
  }

  for (const document of documents) {
    const check = await checkJsonFile(path.join(outputRoot, document.fileName), document.document);
    errors.push(...check.errors);
  }

  return {
    upToDate: errors.length === 0,
    errors
  };
}

function getHelpText() {
  return `Usage:
  npm run html52:css-coverage -- [options]

Options:
  --registry <path>       CSS module registry. Defaults to tests/html52/generated/css-modules/registry.json.
  --output <path>         Coverage output. Defaults to tests/html52/coverage/css-modules.json.
  --implementation-output-root <dir>
                          Directory for per-family CSS implementation status files.
                          Defaults to tests/html52/coverage/css-module-implementation.
  --check                 Exit non-zero if the output file is out of date.
  -h, --help              Show this help text.`;
}

export {
  createCoverageMap,
  createImplementationDocuments,
  main,
  parseArguments
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
