#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  countBy,
  defaultManifestPath,
  defaultOutputRoot,
  fileExists,
  formatList,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  readManifestWithGenerated,
  repositoryRoot,
  resolveFrom,
  writeJson,
  writeText
} from './common.mjs';
import { validateManifest } from './validate-manifest.mjs';

const executableStatuses = new Set(['active', 'optional', 'quarantined']);
const supportedExpectations = new Map([
  ['tokens', 'dump-tokens'],
  ['dom', 'dump-dom'],
  ['css', 'dump-css'],
  ['computedStyle', 'dump-computed-style'],
  ['layout', 'dump-layout'],
  ['displayList', 'dump-display-list'],
  ['resourceLog', 'dump-resources'],
  ['render', 'render']
]);
const defaultToolProjectPath = path.join(repositoryRoot, 'Source', 'Broiler.HTML.Tool', 'Broiler.HTML.Tool.csproj');

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
      checkFiles: true
    });
    if (validation.errors.length > 0) {
      console.error(`Manifest validation failed with ${validation.errors.length} error(s).`);
      for (const error of validation.errors) {
        console.error(`- ${error}`);
      }
      return 1;
    }

    const selectedCases = selectCases(manifest.cases, options);
    console.log(`${options.dryRun ? 'Dry run selected' : 'Selected'} ${selectedCases.length} case(s) from ${manifest.cases.length} manifest case(s).`);
    console.log(`Manifest: ${normalizePathForDisplay(options.manifestPath)}`);
    console.log(`Output: ${normalizePathForDisplay(options.output)}`);
    console.log(`Include filters: ${formatList(options.includes)}`);
    console.log(`Exclude filters: ${formatList(options.excludes)}`);
    console.log(`Cluster filters: ${formatList(options.clusters)}`);
    console.log(`CSS module filters: ${formatList(options.cssModules)}`);
    console.log(`Case filters: ${formatList(options.caseIds)}`);

    if (selectedCases.length > 0) {
      console.log();
      for (const testCase of selectedCases) {
        console.log(`- ${testCase.id} [${testCase.cluster}] ${testCase.title}`);
      }
    } else {
      console.log();
      console.log('No cases selected. Phase 0 seeds the harness before executable fixtures are added.');
    }

    let results = [];
    if (!options.dryRun && selectedCases.length > 0) {
      if (!options.skipBuild) {
        console.log();
        console.log('Building Broiler.HTML.Tool once before parser cases.');
        runCommand(options.dotnet, ['build', options.toolProject, '-nologo'], 'Build Broiler.HTML.Tool');
      }

      results = await executeCases(selectedCases, options, manifest.defaults);
    }

    const summary = createRunSummary(manifest, options, selectedCases, validation, results);
    await writeRunSummary(options.output, summary);
    console.log(`HTML 5.2 suite result: ${summary.passedCount} passed, ${summary.failedCount} failed, ${summary.notRunCount} not run.`);

    if (!options.dryRun && summary.failedCount > 0) {
      console.error(`${summary.failedCount} HTML 5.2 case(s) failed.`);
      return 1;
    }

    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

function parseArguments(argv) {
  const options = {
    manifestPath: defaultManifestPath,
    output: defaultOutputRoot,
    includes: [],
    excludes: [],
    clusters: [],
    cssModules: [],
    caseIds: [],
    statuses: [],
    dotnet: 'dotnet',
    toolProject: defaultToolProjectPath,
    repeat: 1,
    skipBuild: false,
    dryRun: false,
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
      case '--output':
      case '-o': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.output = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--include': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.includes.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--exclude': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.excludes.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--cluster': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.clusters.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--css-module': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.cssModules.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--case': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.caseIds.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--status': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.statuses.push(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--dotnet': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.dotnet = parsed.value;
        index = parsed.nextIndex;
        break;
      }
      case '--tool-project': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.toolProject = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--repeat': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.repeat = parsePositiveInteger(parsed.value, '--repeat');
        index = parsed.nextIndex;
        break;
      }
      case '--skip-build':
        options.skipBuild = true;
        break;
      case '--dry-run':
        options.dryRun = true;
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
  options.output = path.resolve(options.output);
  options.toolProject = path.resolve(options.toolProject);
  return options;
}

function selectCases(cases, options) {
  return cases.filter((testCase) => {
    if (options.statuses.length === 0 && !executableStatuses.has(testCase.status)) {
      return false;
    }
    if (options.clusters.length > 0 && !options.clusters.includes(testCase.cluster)) {
      return false;
    }
    if (options.cssModules.length > 0 && !matchesCssModuleFilter(testCase, options.cssModules)) {
      return false;
    }
    if (options.caseIds.length > 0 && !options.caseIds.includes(testCase.id)) {
      return false;
    }
    if (options.statuses.length > 0 && !options.statuses.includes(testCase.status)) {
      return false;
    }

    const searchText = [
      testCase.id,
      testCase.title,
      testCase.cluster,
      testCase.subcluster,
      testCase.featureId,
      testCase.input,
      ...(testCase.spec ?? []).flatMap((reference) => [reference.section, reference.url])
    ].filter(Boolean).join('\n').toLowerCase();

    if (options.includes.length > 0 && !options.includes.some((include) => searchText.includes(include.toLowerCase()))) {
      return false;
    }
    if (options.excludes.some((exclude) => searchText.includes(exclude.toLowerCase()))) {
      return false;
    }

    return true;
  });
}

function matchesCssModuleFilter(testCase, cssModules) {
  const normalizedFilters = cssModules.map(normalizeCssModuleFilter);
  const candidates = [
    testCase.id,
    testCase.subcluster,
    testCase.featureId,
    testCase.input,
    ...(testCase.spec ?? []).flatMap((reference) => [reference.section, reference.url])
  ].filter(Boolean).map((value) => value.toLowerCase());

  return normalizedFilters.some((filter) => candidates.some((candidate) =>
    candidate === filter
    || candidate === `css-module-${filter}`
    || candidate.includes(`/tr/${filter}/`)
    || candidate.includes(`/tr/${filter}`)
    || candidate.includes(`/css-${filter}/`)
    || candidate.includes(filter)));
}

function normalizeCssModuleFilter(value) {
  return value.toLowerCase().replace(/^css-module-/, '');
}

async function executeCases(selectedCases, options, defaults) {
  const results = [];
  for (let iteration = 1; iteration <= options.repeat; iteration++) {
    for (const testCase of selectedCases) {
      results.push(await executeCase(testCase, options, defaults, iteration));
    }
  }
  return results;
}

async function executeCase(testCase, options, defaults, iteration = 1) {
  const caseDirectory = options.repeat > 1
    ? path.join(options.output, 'cases', testCase.id, `repeat-${String(iteration).padStart(2, '0')}`)
    : path.join(options.output, 'cases', testCase.id);
  await fs.mkdir(caseDirectory, { recursive: true });

  const expectationEntries = Object.entries(testCase.expectations ?? {})
    .filter(([expectationName]) => supportedExpectations.has(expectationName));

  if (expectationEntries.length === 0) {
    return {
      id: testCase.id,
      title: testCase.title,
      cluster: testCase.cluster,
      status: testCase.status,
      result: 'not-run',
      reason: 'No supported expectation handler is available for this case.',
      iteration,
      checks: []
    };
  }

  const checks = [];
  for (const [expectationName, expectationPath] of expectationEntries) {
    checks.push(expectationName === 'render'
      ? await executeRenderExpectation(testCase, expectationPath, caseDirectory, options, defaults)
      : await executeJsonExpectation(testCase, expectationName, expectationPath, caseDirectory, options, defaults));
  }

  const failedChecks = checks.filter((check) => check.result === 'failed');
  return {
    id: testCase.id,
    title: testCase.title,
    cluster: testCase.cluster,
    status: testCase.status,
    iteration,
    result: failedChecks.length === 0 ? 'passed' : 'failed',
    checks
  };
}

async function executeRenderExpectation(testCase, expectationPath, caseDirectory, options, defaults) {
  const viewport = (testCase.viewports ?? defaults?.viewports ?? [{ width: 800, height: 600, deviceScaleFactor: 1 }])[0];
  const tolerance = testCase.tolerance ?? defaults?.tolerance ?? { pixelDiffThreshold: 0.001, colorTolerance: 2 };
  const actualPath = path.join(caseDirectory, 'render.actual.png');
  const diffPath = path.join(caseDirectory, 'render.diff.png');
  const reportPath = path.join(caseDirectory, 'render.report.json');
  const expectedPath = resolveFrom(options.manifestPath, expectationPath);
  const inputPath = resolveFrom(options.manifestPath, testCase.input);

  if (!(await fileExists(expectedPath))) {
    return {
      expectation: 'render',
      result: 'failed',
      reason: `Expected PNG file does not exist: ${expectationPath}`,
      expectedPath: normalizePathForDisplay(expectedPath),
      actualPath: normalizePathForDisplay(actualPath)
    };
  }

  const startedAt = Date.now();
  const renderResult = runCommand(options.dotnet, [
    'run',
    '--no-build',
    '--project', options.toolProject,
    '--',
    'render',
    '--input', inputPath,
    '--output', actualPath,
    '--width', String(viewport.width),
    '--height', String(viewport.height),
    '--disable-network'
  ], `render ${testCase.id}`, { allowExitCodes: [0] });

  if (renderResult.status !== 0) {
    return {
      expectation: 'render',
      result: 'failed',
      reason: `render exited with status ${renderResult.status}.`,
      stdout: renderResult.stdout,
      stderr: renderResult.stderr,
      expectedPath: normalizePathForDisplay(expectedPath),
      actualPath: normalizePathForDisplay(actualPath),
      durationMs: Date.now() - startedAt
    };
  }

  const compareResult = runCommand(options.dotnet, [
    'run',
    '--no-build',
    '--project', options.toolProject,
    '--',
    'compare',
    '--actual', actualPath,
    '--baseline', expectedPath,
    '--diff-output', diffPath,
    '--json-output', reportPath,
    '--pixel-diff-threshold', String(tolerance.pixelDiffThreshold),
    '--color-tolerance', String(tolerance.colorTolerance)
  ], `compare render ${testCase.id}`, { allowExitCodes: [0, 1] });

  let report = null;
  if (await fileExists(reportPath)) {
    report = await readJson(reportPath);
  }

  return {
    expectation: 'render',
    result: compareResult.status === 0 ? 'passed' : 'failed',
    reason: compareResult.status === 0 ? null : 'Rendered PNG does not match expected baseline.',
    expectedPath: normalizePathForDisplay(expectedPath),
    actualPath: normalizePathForDisplay(actualPath),
    diffPath: await fileExists(diffPath) ? normalizePathForDisplay(diffPath) : null,
    reportPath: normalizePathForDisplay(reportPath),
    diffRatio: normalizeCompareNumber(readReportProperty(report, 'diffRatio')),
    viewport,
    tolerance,
    durationMs: Date.now() - startedAt
  };
}

async function executeJsonExpectation(testCase, expectationName, expectationPath, caseDirectory, options, defaults) {
  const command = supportedExpectations.get(expectationName);
  const actualPath = path.join(caseDirectory, `${expectationName}.actual.json`);
  const expectedPath = resolveFrom(options.manifestPath, expectationPath);
  const inputPath = resolveFrom(options.manifestPath, testCase.input);

  if (!(await fileExists(expectedPath))) {
    return {
      expectation: expectationName,
      result: 'failed',
      reason: `Expected JSON file does not exist: ${expectationPath}`,
      expectedPath: normalizePathForDisplay(expectedPath),
      actualPath: normalizePathForDisplay(actualPath)
    };
  }

  const args = [
    'run',
    '--no-build',
    '--project', options.toolProject,
    '--',
    command,
    '--input', inputPath,
    '--output', actualPath
  ];
  if (expectationName === 'dom' || expectationName === 'resourceLog' || expectationName === 'computedStyle' || expectationName === 'layout' || expectationName === 'displayList') {
    args.push('--base-url', new URL(`file:///${inputPath.replaceAll(path.sep, '/')}`).href);
  }
  if (expectationName === 'computedStyle' || expectationName === 'layout' || expectationName === 'displayList') {
    const viewport = (testCase.viewports ?? defaults?.viewports ?? [{ width: 800, height: 600, deviceScaleFactor: 1 }])[0];
    args.push('--width', String(viewport.width), '--height', String(viewport.height), '--disable-network');
  }

  const startedAt = Date.now();
  const commandResult = runCommand(options.dotnet, args, `${command} ${testCase.id}`, { allowExitCodes: [0] });
  const durationMs = Date.now() - startedAt;

  if (commandResult.status !== 0) {
    return {
      expectation: expectationName,
      result: 'failed',
      reason: `${command} exited with status ${commandResult.status}.`,
      stdout: commandResult.stdout,
      stderr: commandResult.stderr,
      expectedPath: normalizePathForDisplay(expectedPath),
      actualPath: normalizePathForDisplay(actualPath),
      durationMs
    };
  }

  const [expectedJson, actualJson] = await Promise.all([
    readJson(expectedPath),
    readJson(actualPath)
  ]);
  const expectedNormalized = stableStringify(expectedJson);
  const actualNormalized = stableStringify(actualJson);
  const passed = expectedNormalized === actualNormalized;

  if (!passed) {
    await writeText(path.join(caseDirectory, `${expectationName}.expected.normalized.json`), `${expectedNormalized}\n`);
    await writeText(path.join(caseDirectory, `${expectationName}.actual.normalized.json`), `${actualNormalized}\n`);
  }

  return {
    expectation: expectationName,
    result: passed ? 'passed' : 'failed',
    reason: passed ? null : 'Actual JSON does not match expected JSON.',
    expectedPath: normalizePathForDisplay(expectedPath),
    actualPath: normalizePathForDisplay(actualPath),
    durationMs
  };
}

function createRunSummary(manifest, options, selectedCases, validation, results = []) {
  const passed = results.filter((result) => result.result === 'passed');
  const failed = results.filter((result) => result.result === 'failed');
  const notRun = options.dryRun
    ? selectedCases.map((testCase) => ({
      id: testCase.id,
      title: testCase.title,
      cluster: testCase.cluster,
      status: testCase.status,
      result: 'not-run',
      reason: 'Dry-run mode does not execute cases.',
      checks: []
    }))
    : results.filter((result) => result.result === 'not-run');

  return {
    generatedAt: new Date().toISOString(),
    suite: manifest.suite,
    manifestPath: normalizePathForDisplay(options.manifestPath),
    outputRoot: normalizePathForDisplay(options.output),
    dryRun: options.dryRun,
    repeat: options.repeat,
    filters: {
      includes: options.includes,
      excludes: options.excludes,
      clusters: options.clusters,
      cssModules: options.cssModules,
      caseIds: options.caseIds,
      statuses: options.statuses
    },
    tool: {
      dotnet: options.dotnet,
      toolProject: normalizePathForDisplay(options.toolProject),
      skipBuild: options.skipBuild
    },
    totalCases: manifest.cases.length,
    selectedCount: selectedCases.length,
    selected: selectedCases.map((testCase) => ({
      id: testCase.id,
      title: testCase.title,
      cluster: testCase.cluster,
      status: testCase.status,
      input: testCase.input
    })),
    statusCounts: countBy(selectedCases, (testCase) => testCase.status),
    clusterCounts: countBy(selectedCases, (testCase) => testCase.cluster),
    validationWarnings: validation.warnings,
    executedCount: results.filter((result) => result.result !== 'not-run').length,
    passedCount: passed.length,
    failedCount: failed.length,
    notRunCount: notRun.length,
    notRunReason: options.dryRun
      ? 'Dry-run mode does not execute cases.'
      : 'Selected cases without supported expectations are not run.',
    passed,
    failed,
    notRun,
    results
  };
}

async function writeRunSummary(outputRoot, summary) {
  await writeJson(path.join(outputRoot, 'summary.json'), summary);
  await writeText(path.join(outputRoot, 'summary.md'), createSummaryMarkdown(summary));
}

function createSummaryMarkdown(summary) {
  const lines = [
    '# HTML 5.2 suite run summary',
    '',
    `- Manifest: \`${summary.manifestPath}\``,
    `- Output root: \`${summary.outputRoot}\``,
    `- Dry run: ${summary.dryRun ? 'yes' : 'no'}`,
    `- Repeat count: ${summary.repeat}`,
    `- Total manifest cases: ${summary.totalCases}`,
    `- Selected cases: ${summary.selectedCount}`,
    `- Executed cases: ${summary.executedCount}`,
    `- Passed: ${summary.passedCount}`,
    `- Failed: ${summary.failedCount}`,
    `- Not run: ${summary.notRunCount}`,
    `- Not-run reason: ${summary.notRunReason}`,
    ''
  ];

  if (summary.failed.length > 0) {
    lines.push('## Failures', '', '| Case | Check | Reason | Actual | Expected |', '| --- | --- | --- | --- | --- |');
    for (const failure of summary.failed) {
      for (const check of failure.checks.filter((item) => item.result === 'failed')) {
        lines.push(`| \`${failure.id}\` | ${check.expectation} | ${check.reason ?? 'n/a'}${check.diffRatio === null || check.diffRatio === undefined ? '' : ` (diff ratio ${check.diffRatio})`} | \`${check.actualPath ?? 'n/a'}\` | \`${check.expectedPath ?? 'n/a'}\` |`);
      }
    }
    lines.push('');
  }

  if (summary.selected.length > 0) {
    lines.push('## Selected Cases', '', '| Case | Cluster | Status | Input |', '| --- | --- | --- | --- |');
    for (const testCase of summary.selected) {
      lines.push(`| \`${testCase.id}\` | ${testCase.cluster} | ${testCase.status} | \`${testCase.input}\` |`);
    }
    lines.push('');
  } else {
    lines.push('No cases were selected.', '');
  }

  if (summary.validationWarnings.length > 0) {
    lines.push('## Validation Warnings', '');
    for (const warning of summary.validationWarnings) {
      lines.push(`- ${warning}`);
    }
    lines.push('');
  }

  return `${lines.join('\n')}\n`;
}

function getHelpText() {
  return `Usage:
  npm run html52:run -- [options]

Options:
  --manifest <path>       Manifest to load. Defaults to tests/html52/manifest.json.
  --output <dir>          Summary output directory. Defaults to artifacts/html52.
  --include <text>        Select cases whose metadata contains text. Repeat as needed.
  --exclude <text>        Skip cases whose metadata contains text. Repeat as needed.
  --cluster <id>          Select a cluster. Repeat as needed.
  --css-module <id>       Select cases for a CSS module id, e.g. css-cascade-5.
  --case <id>             Select an exact case id. Repeat as needed.
  --status <status>       Select a case status. Repeat as needed.
  --dotnet <path>         dotnet executable. Defaults to dotnet.
  --tool-project <path>   Broiler.HTML.Tool project path.
  --repeat <count>        Execute selected cases this many times. Defaults to 1.
  --skip-build            Skip the pre-run tool build.
  --dry-run               Validate and list selected cases without execution.
  -h, --help              Show this help text.

Notes:
  - When --status is omitted, only active, optional, and quarantined cases are selected.
  - Phase 1 executes token, DOM, CSS, computed-style, layout, and display-list JSON expectations through Broiler.HTML.Tool.
  - Phase 2 executes render PNG expectations through Broiler.HTML.Tool render/compare.
  - Phase 4 executes resourceLog JSON expectations through Broiler.HTML.Tool dump-resources.
  - Phase 5 adds CSS, text/i18n, and paint-depth render or DOM oracles.
  - Phase 6 adds XHTML/XML, legacy, stress/security, coverage-close, and repeatability cases.`;
}

function parsePositiveInteger(value, argumentName) {
  const parsed = Number.parseInt(value, 10);
  if (!Number.isInteger(parsed) || String(parsed) !== String(value).trim() || parsed < 1) {
    throw new Error(`${argumentName} must be a positive integer.`);
  }
  return parsed;
}

function runCommand(command, args, description, options = {}) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    windowsHide: true,
    maxBuffer: 20 * 1024 * 1024
  });

  if (result.error) {
    throw new Error(`${description} failed to start: ${result.error.message}`);
  }

  const status = result.status ?? 1;
  const allowExitCodes = options.allowExitCodes ?? [0];
  if (!allowExitCodes.includes(status)) {
    throw new Error([
      `${description} failed with exit code ${status}.`,
      result.stdout ? `stdout:\n${result.stdout}` : null,
      result.stderr ? `stderr:\n${result.stderr}` : null
    ].filter(Boolean).join('\n\n'));
  }

  return {
    status,
    stdout: result.stdout,
    stderr: result.stderr
  };
}

function stableStringify(value) {
  return JSON.stringify(sortJson(value), null, 2);
}

function sortJson(value) {
  if (Array.isArray(value)) {
    return value.map(sortJson);
  }
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value)
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([key, child]) => [key, sortJson(child)])
    );
  }
  return value;
}

function readReportProperty(report, name) {
  if (!report || typeof report !== 'object') {
    return null;
  }

  const pascalName = `${name.charAt(0).toUpperCase()}${name.slice(1)}`;
  return report[name] ?? report[pascalName] ?? null;
}

function normalizeCompareNumber(value) {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

export {
  createRunSummary,
  createSummaryMarkdown,
  main,
  parseArguments,
  selectCases
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
