#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  countBy,
  defaultCoverageRoot,
  defaultManifestPath,
  defaultOutputRoot,
  formatList,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  readManifestWithGenerated,
  writeJson,
  writeText
} from './common.mjs';
import { validateManifest } from './validate-manifest.mjs';

const coverageFiles = [
  { name: 'spec-map', fileName: 'spec-map.json' },
  { name: 'elements', fileName: 'elements.json' },
  { name: 'attributes', fileName: 'attributes.json' },
  { name: 'css-requirements', fileName: 'css-requirements.json' },
  { name: 'css-modules', fileName: 'css-modules.json' },
  { name: 'out-of-scope', fileName: 'out-of-scope.json' }
];
const actionableSupportLevels = new Set(['required', 'recommended']);

async function main(argv = process.argv.slice(2)) {
  try {
    const options = parseArguments(argv);
    if (options.help) {
      console.log(getHelpText());
      return 0;
    }

    const manifest = await readManifestWithGenerated(options.manifestPath);
    const validation = await validateManifest(manifest, {
      manifestPath: options.manifestPath,
      checkFiles: false
    });
    if (validation.errors.length > 0) {
      console.error(`Manifest validation failed with ${validation.errors.length} error(s).`);
      for (const error of validation.errors) {
        console.error(`- ${error}`);
      }
      return 1;
    }

    const coverageItems = await loadCoverageItems(options.coverageRoot);
    const summary = createCoverageSummary(manifest, coverageItems, options);
    await writeCoverageSummary(options.output, summary);

    printCoverageSummary(summary);
    return options.failOnUncovered && summary.uncoveredPlannedCount > 0 ? 1 : 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

function parseArguments(argv) {
  const options = {
    manifestPath: defaultManifestPath,
    coverageRoot: defaultCoverageRoot,
    output: defaultOutputRoot,
    failOnUncovered: false,
    help: false
  };

  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (argument === '--') {
      continue;
    }
    const name = argument.includes('=') ? argument.slice(0, argument.indexOf('=')) : argument;

    switch (name) {
      case '--manifest':
      case '-m': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.manifestPath = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--coverage-root': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.coverageRoot = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--output':
      case '-o': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.output = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--fail-on-uncovered':
        options.failOnUncovered = true;
        break;
      case '-h':
      case '--help':
        options.help = true;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  options.manifestPath = path.resolve(options.manifestPath);
  options.coverageRoot = path.resolve(options.coverageRoot);
  options.output = path.resolve(options.output);
  return options;
}

async function loadCoverageItems(coverageRoot) {
  const items = [];
  for (const coverageFile of coverageFiles) {
    const filePath = path.join(coverageRoot, coverageFile.fileName);
    const document = await readJson(filePath);
    if (!Array.isArray(document.items)) {
      throw new Error(`${filePath} must contain an items array.`);
    }

    for (const [index, item] of document.items.entries()) {
      items.push(normalizeCoverageItem(item, coverageFile.name, index));
    }
  }
  return items;
}

function normalizeCoverageItem(item, source, index) {
  if (!item || typeof item !== 'object' || Array.isArray(item)) {
    throw new Error(`${source}.items[${index}] must be an object.`);
  }
  if (typeof item.featureId !== 'string' || item.featureId.trim() === '') {
    throw new Error(`${source}.items[${index}].featureId must be a non-empty string.`);
  }

  return {
    source,
    featureId: item.featureId,
    title: item.title ?? item.featureId,
    cluster: item.cluster ?? 'unclassified',
    specSections: Array.isArray(item.specSections) ? item.specSections : [],
    supportLevel: item.supportLevel ?? 'required',
    status: item.status ?? 'planned',
    tests: Array.isArray(item.tests) ? item.tests : [],
    targetMinimum: Number.isFinite(item.targetMinimum) ? item.targetMinimum : null,
    reason: item.reason ?? null,
    moduleId: item.moduleId ?? null,
    moduleTitle: item.moduleTitle ?? null,
    moduleFamily: item.moduleFamily ?? null,
    w3cStatus: item.w3cStatus ?? null,
    raw: item
  };
}

function createCoverageSummary(manifest, coverageItems, options) {
  const caseIds = new Set(manifest.cases.map((testCase) => testCase.id));
  const caseFeatureIds = new Map();
  for (const testCase of manifest.cases) {
    if (testCase.featureId) {
      caseFeatureIds.set(testCase.featureId, [...(caseFeatureIds.get(testCase.featureId) ?? []), testCase.id]);
    }
  }

  const normalizedItems = coverageItems.map((item) => {
    const linkedTests = [
      ...item.tests.filter((testId) => caseIds.has(testId)),
      ...(caseFeatureIds.get(item.featureId) ?? [])
    ];
    const missingTests = item.tests.filter((testId) => !caseIds.has(testId));
    const outOfScope = item.status === 'out-of-scope' || item.supportLevel === 'out-of-scope';
    const covered = outOfScope || linkedTests.length > 0;

    return {
      source: item.source,
      featureId: item.featureId,
      title: item.title,
      cluster: item.cluster,
      specSections: item.specSections,
      supportLevel: item.supportLevel,
      status: item.status,
      targetMinimum: item.targetMinimum,
      covered,
      linkedTests,
      missingTests,
      reason: item.reason,
      moduleId: item.moduleId,
      moduleTitle: item.moduleTitle,
      moduleFamily: item.moduleFamily,
      w3cStatus: item.w3cStatus
    };
  });

  const requiredItems = normalizedItems.filter((item) => actionableSupportLevels.has(item.supportLevel));
  const uncoveredPlanned = requiredItems.filter((item) => !item.covered && item.status === 'planned');
  const outOfScope = normalizedItems.filter((item) => item.supportLevel === 'out-of-scope' || item.status === 'out-of-scope');
  const cssModules = normalizedItems.filter((item) => item.source === 'css-modules');
  const uncoveredCssModules = cssModules.filter((item) => actionableSupportLevels.has(item.supportLevel) && !item.covered);

  return {
    generatedAt: new Date().toISOString(),
    manifestPath: normalizePathForDisplay(options.manifestPath),
    coverageRoot: normalizePathForDisplay(options.coverageRoot),
    outputRoot: normalizePathForDisplay(options.output),
    suite: manifest.suite,
    manifestCaseCount: manifest.cases.length,
    coverageItemCount: normalizedItems.length,
    coveredCount: normalizedItems.filter((item) => item.covered).length,
    uncoveredPlannedCount: uncoveredPlanned.length,
    outOfScopeCount: outOfScope.length,
    statusCounts: countBy(normalizedItems, (item) => item.status),
    supportLevelCounts: countBy(normalizedItems, (item) => item.supportLevel),
    sourceCounts: countBy(normalizedItems, (item) => item.source),
    clusterCounts: countBy(normalizedItems, (item) => item.cluster),
    cssModuleCoverage: {
      itemCount: cssModules.length,
      coveredCount: cssModules.filter((item) => item.covered).length,
      uncoveredCount: uncoveredCssModules.length,
      outOfScopeCount: cssModules.filter((item) => item.supportLevel === 'out-of-scope' || item.status === 'out-of-scope').length,
      byW3cStatus: countBy(cssModules, (item) => item.w3cStatus ?? 'unknown'),
      bySupportLevel: countBy(cssModules, (item) => item.supportLevel),
      byFamily: countBy(cssModules, (item) => item.moduleFamily ?? 'unclassified'),
      uncovered: uncoveredCssModules
    },
    uncoveredPlanned,
    outOfScope,
    items: normalizedItems
  };
}

async function writeCoverageSummary(outputRoot, summary) {
  await writeJson(path.join(outputRoot, 'coverage.json'), summary);
  await writeText(path.join(outputRoot, 'coverage.md'), createCoverageMarkdown(summary));
}

function printCoverageSummary(summary) {
  console.log('HTML 5.2 coverage summary');
  console.log(`Manifest: ${summary.manifestPath}`);
  console.log(`Coverage root: ${summary.coverageRoot}`);
  console.log(`Output: ${summary.outputRoot}`);
  console.log(`Manifest cases: ${summary.manifestCaseCount}`);
  console.log(`Coverage items: ${summary.coverageItemCount}`);
  console.log(`Covered items: ${summary.coveredCount}`);
  console.log(`Uncovered planned items: ${summary.uncoveredPlannedCount}`);
  console.log(`Out-of-scope items: ${summary.outOfScopeCount}`);
  console.log(`Sources: ${formatObjectCounts(summary.sourceCounts)}`);
  console.log(`Support levels: ${formatObjectCounts(summary.supportLevelCounts)}`);
  console.log(`CSS module rows: ${summary.cssModuleCoverage.itemCount}`);
  console.log(`Uncovered CSS module rows: ${summary.cssModuleCoverage.uncoveredCount}`);
  console.log();

  if (summary.uncoveredPlanned.length > 0) {
    console.log('Uncovered planned features:');
    for (const item of summary.uncoveredPlanned) {
      console.log(`- ${item.featureId} [${item.cluster}] ${item.title}`);
    }
  } else {
    console.log('No uncovered planned features.');
  }
}

function createCoverageMarkdown(summary) {
  const lines = [
    '# HTML 5.2 coverage summary',
    '',
    `- Manifest: \`${summary.manifestPath}\``,
    `- Coverage root: \`${summary.coverageRoot}\``,
    `- Manifest cases: ${summary.manifestCaseCount}`,
    `- Coverage items: ${summary.coverageItemCount}`,
    `- Covered items: ${summary.coveredCount}`,
    `- Uncovered planned items: ${summary.uncoveredPlannedCount}`,
    `- Out-of-scope items: ${summary.outOfScopeCount}`,
    `- Sources: ${formatObjectCounts(summary.sourceCounts)}`,
    `- Support levels: ${formatObjectCounts(summary.supportLevelCounts)}`,
    `- CSS module rows: ${summary.cssModuleCoverage.itemCount}`,
    `- Uncovered required/recommended CSS module rows: ${summary.cssModuleCoverage.uncoveredCount}`,
    ''
  ];

  if (summary.uncoveredPlanned.length > 0) {
    lines.push('## Uncovered Planned Features', '', '| Feature | Cluster | Source | Support | Spec |', '| --- | --- | --- | --- | --- |');
    for (const item of summary.uncoveredPlanned) {
      lines.push(`| \`${item.featureId}\` | ${item.cluster} | ${item.source} | ${item.supportLevel} | ${formatList(item.specSections)} |`);
    }
    lines.push('');
  }

  if (summary.outOfScope.length > 0) {
    lines.push('## Out Of Scope', '', '| Feature | Cluster | Reason |', '| --- | --- | --- |');
    for (const item of summary.outOfScope) {
      lines.push(`| \`${item.featureId}\` | ${item.cluster} | ${item.reason ?? 'n/a'} |`);
    }
    lines.push('');
  }

  if (summary.cssModuleCoverage.itemCount > 0) {
    lines.push(
      '## CSS Module Coverage',
      '',
      `- W3C statuses: ${formatObjectCounts(summary.cssModuleCoverage.byW3cStatus)}`,
      `- Support levels: ${formatObjectCounts(summary.cssModuleCoverage.bySupportLevel)}`,
      `- Families: ${formatObjectCounts(summary.cssModuleCoverage.byFamily)}`,
      ''
    );

    if (summary.cssModuleCoverage.uncovered.length > 0) {
      lines.push('| Feature | W3C status | Support | Family |', '| --- | --- | --- | --- |');
      for (const item of summary.cssModuleCoverage.uncovered) {
        lines.push(`| \`${item.featureId}\` | ${item.w3cStatus ?? 'n/a'} | ${item.supportLevel} | ${item.moduleFamily ?? 'n/a'} |`);
      }
      lines.push('');
    }
  }

  return `${lines.join('\n')}\n`;
}

function formatObjectCounts(counts) {
  const entries = Object.entries(counts);
  return entries.length > 0
    ? entries.map(([key, value]) => `${key}: ${value}`).join(', ')
    : 'none';
}

function getHelpText() {
  return `Usage:
  npm run html52:coverage -- [options]

Options:
  --manifest <path>       Manifest to read. Defaults to tests/html52/manifest.json.
  --coverage-root <dir>   Coverage map directory. Defaults to tests/html52/coverage.
  --output <dir>          Summary output directory. Defaults to artifacts/html52.
  --fail-on-uncovered     Exit non-zero when planned coverage items have no tests.
  -h, --help              Show this help text.`;
}

export {
  createCoverageMarkdown,
  createCoverageSummary,
  loadCoverageItems,
  main,
  parseArguments
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
