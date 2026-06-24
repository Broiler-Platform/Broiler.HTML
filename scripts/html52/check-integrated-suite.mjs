#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultManifestPath,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  readManifestWithGenerated,
  resolveFrom
} from './common.mjs';
import { selectCases } from './run-suite.mjs';
import { validateManifest } from './validate-manifest.mjs';

const defaultCssModuleManifest = 'generated/css-modules/manifest.generated.json';
const executableStatuses = new Set(['active', 'optional', 'quarantined']);

async function main(argv = process.argv.slice(2)) {
  try {
    const options = parseArguments(argv);
    if (options.help) {
      console.log(getHelpText());
      return 0;
    }

    const errors = [];
    const rootManifest = await readJson(options.manifestPath);
    const generatedManifests = Array.isArray(rootManifest.generatedManifests)
      ? rootManifest.generatedManifests
      : [];

    if (!generatedManifests.includes(options.cssModuleManifest)) {
      errors.push(`Root manifest must include ${options.cssModuleManifest} in generatedManifests.`);
    }

    const mergedManifest = await readManifestWithGenerated(options.manifestPath);
    const validation = await validateManifest(mergedManifest, {
      manifestPath: options.manifestPath,
      checkFiles: true
    });
    errors.push(...validation.errors);

    const cssModuleManifestPath = resolveFrom(options.manifestPath, options.cssModuleManifest);
    const cssModuleManifest = await readJson(cssModuleManifestPath);
    if (!Array.isArray(cssModuleManifest.cases)) {
      errors.push(`${options.cssModuleManifest} must contain a cases array.`);
    }

    const generatedCases = Array.isArray(cssModuleManifest.cases)
      ? cssModuleManifest.cases
      : [];
    const generatedCaseIds = new Set(generatedCases.map((testCase) => testCase.id).filter(Boolean));
    const mergedCaseIds = new Set(mergedManifest.cases.map((testCase) => testCase.id));
    const missingFromMerged = [...generatedCaseIds].filter((caseId) => !mergedCaseIds.has(caseId));
    if (missingFromMerged.length > 0) {
      errors.push(`Generated CSS module cases missing from merged manifest: ${missingFromMerged.join(', ')}`);
    }

    const comprehensiveSelection = selectCases(mergedManifest.cases, {
      statuses: [],
      clusters: [],
      cssModules: [],
      caseIds: [],
      includes: [],
      excludes: []
    });
    const selectedCaseIds = new Set(comprehensiveSelection.map((testCase) => testCase.id));
    const executableGeneratedCaseIds = generatedCases
      .filter((testCase) => executableStatuses.has(testCase.status))
      .map((testCase) => testCase.id)
      .filter(Boolean);
    const missingFromComprehensiveRun = executableGeneratedCaseIds.filter((caseId) => !selectedCaseIds.has(caseId));
    if (missingFromComprehensiveRun.length > 0) {
      errors.push(`Executable CSS module cases missing from comprehensive run selection: ${missingFromComprehensiveRun.join(', ')}`);
    }

    const nonCssModuleCases = generatedCases.filter((testCase) => testCase.cluster !== 'css-modules');
    if (nonCssModuleCases.length > 0) {
      errors.push(`Generated CSS module manifest contains non-css-modules cases: ${nonCssModuleCases.map((testCase) => testCase.id).join(', ')}`);
    }

    if (errors.length > 0) {
      console.error(`Integrated HTML/CSS suite check failed with ${errors.length} error(s):`);
      for (const error of errors) {
        console.error(`- ${error}`);
      }
      return 1;
    }

    const cssModuleCases = mergedManifest.cases.filter((testCase) => testCase.cluster === 'css-modules');
    console.log('Integrated HTML/CSS suite check passed.');
    console.log(`Root manifest: ${normalizePathForDisplay(options.manifestPath)}`);
    console.log(`CSS module manifest: ${normalizePathForDisplay(cssModuleManifestPath)}`);
    console.log(`Merged manifest cases: ${mergedManifest.cases.length}`);
    console.log(`CSS module cases: ${cssModuleCases.length}`);
    console.log(`Comprehensive run selected cases: ${comprehensiveSelection.length}`);
    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

function parseArguments(argv) {
  const options = {
    manifestPath: defaultManifestPath,
    cssModuleManifest: defaultCssModuleManifest,
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
      case '--css-manifest': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.cssModuleManifest = parsed.value;
        index = parsed.nextIndex;
        break;
      }
      case '-h':
      case '--help':
        options.help = true;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  options.manifestPath = path.resolve(options.manifestPath);
  return options;
}

function getHelpText() {
  return `Usage:
  npm run html52:integrated:check -- [options]

Options:
  --manifest <path>       Root manifest to check. Defaults to tests/html52/manifest.json.
  --css-manifest <path>   Generated CSS manifest path relative to the root manifest.
                          Defaults to generated/css-modules/manifest.generated.json.
  -h, --help              Show this help text.`;
}

export {
  main,
  parseArguments
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
