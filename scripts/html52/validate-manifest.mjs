#!/usr/bin/env node
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultManifestPath,
  defaultSchemaPath,
  fileExists,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  readManifestWithGenerated,
  resolveFrom
} from './common.mjs';

const idPattern = /^[a-z0-9][a-z0-9-]*$/;
const caseStatuses = new Set(['planned', 'active', 'optional', 'quarantined', 'blocked', 'out-of-scope']);
const scriptPolicies = new Set(['not-required', 'markup-only', 'forbidden']);
const executableStatuses = new Set(['active', 'optional', 'quarantined']);

async function main(argv = process.argv.slice(2)) {
  try {
    const options = parseArguments(argv);
    if (options.help) {
      console.log(getHelpText());
      return 0;
    }

    await readJson(options.schemaPath);
    const manifest = await readManifestWithGenerated(options.manifestPath);
    const result = await validateManifest(manifest, {
      manifestPath: options.manifestPath,
      checkFiles: options.checkFiles
    });

    if (result.errors.length > 0) {
      console.error(`HTML 5.2 manifest validation failed with ${result.errors.length} error(s):`);
      for (const error of result.errors) {
        console.error(`- ${error}`);
      }
      return 1;
    }

    console.log(`HTML 5.2 manifest is valid: ${normalizePathForDisplay(options.manifestPath)}`);
    console.log(`Cases: ${result.caseCount}`);
    if (result.warnings.length > 0) {
      console.log(`Warnings: ${result.warnings.length}`);
      for (const warning of result.warnings) {
        console.log(`- ${warning}`);
      }
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
    schemaPath: defaultSchemaPath,
    checkFiles: true,
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
      case '--schema': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.schemaPath = path.resolve(parsed.value);
        index = parsed.nextIndex;
        break;
      }
      case '--no-file-check':
        options.checkFiles = false;
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
  options.schemaPath = path.resolve(options.schemaPath);
  return options;
}

async function validateManifest(manifest, options = {}) {
  const manifestPath = path.resolve(options.manifestPath ?? defaultManifestPath);
  const checkFiles = options.checkFiles ?? true;
  const errors = [];
  const warnings = [];

  if (!isPlainObject(manifest)) {
    return {
      errors: ['Manifest root must be an object.'],
      warnings,
      caseCount: 0
    };
  }

  requireInteger(manifest, 'schemaVersion', errors, { minimum: 1 });

  if (!isPlainObject(manifest.suite)) {
    errors.push('suite must be an object.');
  } else {
    requireId(manifest.suite, 'suite.id', errors);
    requireString(manifest.suite, 'suite.title', errors);
    requireString(manifest.suite, 'suite.description', errors);
    if (!isPlainObject(manifest.suite.standard)) {
      errors.push('suite.standard must be an object.');
    } else {
      requireString(manifest.suite.standard, 'suite.standard.name', errors);
      requireString(manifest.suite.standard, 'suite.standard.version', errors);
      requireString(manifest.suite.standard, 'suite.standard.snapshotDate', errors);
      requireUrl(manifest.suite.standard, 'suite.standard.url', errors);
    }
  }

  validateDefaults(manifest.defaults, errors);

  if (manifest.generatedManifests !== undefined) {
    if (!Array.isArray(manifest.generatedManifests)) {
      errors.push('generatedManifests must be an array when present.');
    } else {
      for (const [index, generatedManifest] of manifest.generatedManifests.entries()) {
        if (typeof generatedManifest !== 'string' || generatedManifest.trim() === '') {
          errors.push(`generatedManifests[${index}] must be a non-empty string.`);
          continue;
        }
        if (checkFiles && !(await fileExists(resolveFrom(manifestPath, generatedManifest)))) {
          warnings.push(`generatedManifests[${index}] does not exist yet: ${generatedManifest}`);
        }
      }
    }
  }

  if (!Array.isArray(manifest.cases)) {
    errors.push('cases must be an array.');
    return {
      errors,
      warnings,
      caseCount: 0
    };
  }

  const seenIds = new Set();
  for (const [index, testCase] of manifest.cases.entries()) {
    await validateCase(testCase, index, manifestPath, seenIds, checkFiles, errors, warnings);
  }

  return {
    errors,
    warnings,
    caseCount: manifest.cases.length
  };
}

async function validateCase(testCase, index, manifestPath, seenIds, checkFiles, errors, warnings) {
  const prefix = `cases[${index}]`;
  if (!isPlainObject(testCase)) {
    errors.push(`${prefix} must be an object.`);
    return;
  }

  requireId(testCase, `${prefix}.id`, errors);
  if (typeof testCase.id === 'string') {
    if (seenIds.has(testCase.id)) {
      errors.push(`${prefix}.id duplicates another case id: ${testCase.id}`);
    }
    seenIds.add(testCase.id);
  }

  requireString(testCase, `${prefix}.title`, errors);
  requireId(testCase, `${prefix}.cluster`, errors);
  if (testCase.subcluster !== undefined) {
    requireId(testCase, `${prefix}.subcluster`, errors);
  }
  if (testCase.featureId !== undefined) {
    requireId(testCase, `${prefix}.featureId`, errors);
  }
  requireString(testCase, `${prefix}.input`, errors);

  if (!Array.isArray(testCase.spec) || testCase.spec.length === 0) {
    errors.push(`${prefix}.spec must be a non-empty array.`);
  } else {
    for (const [specIndex, specReference] of testCase.spec.entries()) {
      if (!isPlainObject(specReference)) {
        errors.push(`${prefix}.spec[${specIndex}] must be an object.`);
        continue;
      }
      requireString(specReference, `${prefix}.spec[${specIndex}].section`, errors);
      requireUrl(specReference, `${prefix}.spec[${specIndex}].url`, errors);
    }
  }

  if (!Array.isArray(testCase.assertions) || testCase.assertions.length === 0) {
    errors.push(`${prefix}.assertions must be a non-empty array.`);
  } else {
    for (const [assertionIndex, assertion] of testCase.assertions.entries()) {
      if (typeof assertion !== 'string' || assertion.trim() === '') {
        errors.push(`${prefix}.assertions[${assertionIndex}] must be a non-empty string.`);
      }
    }
  }

  if (!isPlainObject(testCase.expectations) || Object.keys(testCase.expectations).length === 0) {
    errors.push(`${prefix}.expectations must be a non-empty object.`);
  } else {
    for (const [expectationName, expectationPath] of Object.entries(testCase.expectations)) {
      if (typeof expectationPath !== 'string' || expectationPath.trim() === '') {
        errors.push(`${prefix}.expectations.${expectationName} must be a non-empty string.`);
      }
    }
  }

  if (!caseStatuses.has(testCase.status)) {
    errors.push(`${prefix}.status must be one of: ${[...caseStatuses].join(', ')}.`);
  }
  validateQuarantine(testCase, prefix, errors);

  if (!scriptPolicies.has(testCase.scripts)) {
    errors.push(`${prefix}.scripts must be one of: ${[...scriptPolicies].join(', ')}.`);
  }

  if (testCase.viewports !== undefined) {
    validateViewports(testCase.viewports, `${prefix}.viewports`, errors);
  }
  if (testCase.tolerance !== undefined) {
    validateTolerance(testCase.tolerance, `${prefix}.tolerance`, errors);
  }
  if (testCase.fonts !== undefined && !Array.isArray(testCase.fonts)) {
    errors.push(`${prefix}.fonts must be an array when present.`);
  }
  if (testCase.requires !== undefined && !Array.isArray(testCase.requires)) {
    errors.push(`${prefix}.requires must be an array when present.`);
  }

  if (checkFiles && executableStatuses.has(testCase.status)) {
    if (typeof testCase.input === 'string' && !(await fileExists(resolveFrom(manifestPath, testCase.input)))) {
      errors.push(`${prefix}.input does not exist: ${testCase.input}`);
    }
    if (isPlainObject(testCase.expectations)) {
      for (const [expectationName, expectationPath] of Object.entries(testCase.expectations)) {
        if (typeof expectationPath === 'string' && !(await fileExists(resolveFrom(manifestPath, expectationPath)))) {
          errors.push(`${prefix}.expectations.${expectationName} does not exist: ${expectationPath}`);
        }
      }
    }
  } else if (testCase.status === 'planned') {
    warnings.push(`${testCase.id ?? prefix} is planned and will not be file-checked until it becomes executable.`);
  }
}

function validateQuarantine(testCase, prefix, errors) {
  if (testCase.status !== 'quarantined') {
    if (testCase.quarantine !== undefined) {
      errors.push(`${prefix}.quarantine is only allowed when status is quarantined.`);
    }
    return;
  }

  if (!isPlainObject(testCase.quarantine)) {
    errors.push(`${prefix}.quarantine must be an object for quarantined cases.`);
    return;
  }

  requireString(testCase.quarantine, `${prefix}.quarantine.owner`, errors);
  requireString(testCase.quarantine, `${prefix}.quarantine.reason`, errors);
  requireString(testCase.quarantine, `${prefix}.quarantine.expires`, errors);

  const expires = testCase.quarantine.expires;
  if (typeof expires !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(expires)) {
    errors.push(`${prefix}.quarantine.expires must use YYYY-MM-DD format.`);
    return;
  }

  const expiryDate = new Date(`${expires}T00:00:00Z`);
  if (Number.isNaN(expiryDate.getTime())) {
    errors.push(`${prefix}.quarantine.expires must be a valid calendar date.`);
    return;
  }

  const today = new Date();
  const todayUtc = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
  if (expiryDate < todayUtc) {
    errors.push(`${prefix}.quarantine.expires has passed: ${expires}.`);
  }

  if (testCase.quarantine.issue !== undefined && typeof testCase.quarantine.issue !== 'string') {
    errors.push(`${prefix}.quarantine.issue must be a string when present.`);
  }
}

function validateDefaults(defaults, errors) {
  if (!isPlainObject(defaults)) {
    errors.push('defaults must be an object.');
    return;
  }

  validateViewports(defaults.viewports, 'defaults.viewports', errors);

  if (!Array.isArray(defaults.fonts)) {
    errors.push('defaults.fonts must be an array.');
  } else {
    for (const [index, font] of defaults.fonts.entries()) {
      if (typeof font !== 'string' || font.trim() === '') {
        errors.push(`defaults.fonts[${index}] must be a non-empty string.`);
      }
    }
  }

  validateTolerance(defaults.tolerance, 'defaults.tolerance', errors);
}

function validateViewports(viewports, location, errors) {
  if (!Array.isArray(viewports)) {
    errors.push(`${location} must be an array.`);
    return;
  }

  for (const [index, viewport] of viewports.entries()) {
    if (!isPlainObject(viewport)) {
      errors.push(`${location}[${index}] must be an object.`);
      continue;
    }
    requireInteger(viewport, `${location}[${index}].width`, errors, { minimum: 1 });
    requireInteger(viewport, `${location}[${index}].height`, errors, { minimum: 1 });
    requireNumber(viewport, `${location}[${index}].deviceScaleFactor`, errors, { exclusiveMinimum: 0 });
  }
}

function validateTolerance(tolerance, location, errors) {
  if (!isPlainObject(tolerance)) {
    errors.push(`${location} must be an object.`);
    return;
  }

  requireNumber(tolerance, `${location}.pixelDiffThreshold`, errors, { minimum: 0, maximum: 1 });
  requireInteger(tolerance, `${location}.colorTolerance`, errors, { minimum: 0, maximum: 255 });
}

function requireId(object, location, errors) {
  const value = readLocation(object, location);
  if (typeof value !== 'string' || !idPattern.test(value)) {
    errors.push(`${location} must match ${idPattern}.`);
  }
}

function requireString(object, location, errors) {
  const value = readLocation(object, location);
  if (typeof value !== 'string' || value.trim() === '') {
    errors.push(`${location} must be a non-empty string.`);
  }
}

function requireUrl(object, location, errors) {
  const value = readLocation(object, location);
  if (typeof value !== 'string' || value.trim() === '') {
    errors.push(`${location} must be a non-empty URL string.`);
    return;
  }

  try {
    new URL(value);
  } catch {
    errors.push(`${location} must be a valid absolute URL.`);
  }
}

function requireInteger(object, location, errors, options = {}) {
  const value = readLocation(object, location);
  if (!Number.isInteger(value)) {
    errors.push(`${location} must be an integer.`);
    return;
  }
  requireNumberRange(value, location, errors, options);
}

function requireNumber(object, location, errors, options = {}) {
  const value = readLocation(object, location);
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    errors.push(`${location} must be a finite number.`);
    return;
  }
  requireNumberRange(value, location, errors, options);
}

function requireNumberRange(value, location, errors, options) {
  if (options.minimum !== undefined && value < options.minimum) {
    errors.push(`${location} must be >= ${options.minimum}.`);
  }
  if (options.exclusiveMinimum !== undefined && value <= options.exclusiveMinimum) {
    errors.push(`${location} must be > ${options.exclusiveMinimum}.`);
  }
  if (options.maximum !== undefined && value > options.maximum) {
    errors.push(`${location} must be <= ${options.maximum}.`);
  }
}

function readLocation(object, location) {
  const propertyName = location.split('.').at(-1);
  return propertyName ? object[propertyName] : undefined;
}

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function getHelpText() {
  return `Usage:
  npm run html52:validate -- [options]

Options:
  --manifest <path>       Manifest to validate. Defaults to tests/html52/manifest.json.
  --schema <path>         Schema JSON to parse before validation. Defaults to tests/html52/manifest.schema.json.
  --no-file-check         Skip existence checks for executable case inputs and expectations.
  -h, --help              Show this help text.`;
}

export {
  parseArguments,
  validateManifest
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
