import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');
const defaultSuiteRoot = path.join(repositoryRoot, 'tests', 'html52');
const defaultManifestPath = path.join(defaultSuiteRoot, 'manifest.json');
const defaultSchemaPath = path.join(defaultSuiteRoot, 'manifest.schema.json');
const defaultCoverageRoot = path.join(defaultSuiteRoot, 'coverage');
const defaultOutputRoot = path.join(repositoryRoot, 'artifacts', 'html52');

async function readJson(filePath) {
  const absolutePath = path.resolve(filePath);
  try {
    return JSON.parse(await fs.readFile(absolutePath, 'utf8'));
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Failed to read JSON from ${absolutePath}: ${message}`);
  }
}

async function readManifestWithGenerated(manifestPath) {
  const absoluteManifestPath = path.resolve(manifestPath);
  const manifest = await readJson(absoluteManifestPath);
  const generatedManifests = Array.isArray(manifest.generatedManifests)
    ? manifest.generatedManifests
    : [];

  if (generatedManifests.length === 0) {
    return manifest;
  }

  const generatedCases = [];
  for (const generatedManifest of generatedManifests) {
    if (typeof generatedManifest !== 'string' || generatedManifest.trim() === '') {
      continue;
    }

    const generatedManifestPath = resolveFrom(absoluteManifestPath, generatedManifest);
    if (!(await fileExists(generatedManifestPath))) {
      continue;
    }

    const document = await readJson(generatedManifestPath);
    if (!Array.isArray(document.cases)) {
      throw new Error(`${generatedManifestPath} must contain a cases array.`);
    }

    for (const testCase of document.cases) {
      generatedCases.push(testCase);
    }
  }

  return {
    ...manifest,
    cases: [
      ...(Array.isArray(manifest.cases) ? manifest.cases : []),
      ...generatedCases
    ]
  };
}

async function writeJson(filePath, value) {
  const absolutePath = path.resolve(filePath);
  await fs.mkdir(path.dirname(absolutePath), { recursive: true });
  await fs.writeFile(absolutePath, `${JSON.stringify(value, null, 2)}\n`);
}

async function writeText(filePath, value) {
  const absolutePath = path.resolve(filePath);
  await fs.mkdir(path.dirname(absolutePath), { recursive: true });
  await fs.writeFile(absolutePath, value);
}

async function fileExists(filePath) {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

function resolveFrom(basePath, possiblyRelativePath) {
  return path.resolve(path.dirname(path.resolve(basePath)), possiblyRelativePath);
}

function toRepositoryRelative(filePath) {
  return path.relative(repositoryRoot, path.resolve(filePath)).split(path.sep).join('/');
}

function normalizePathForDisplay(filePath) {
  return path.resolve(filePath).split(path.sep).join('/');
}

function parseRepeatedOption(argv, optionName, index) {
  const argument = argv[index];
  const splitIndex = argument.indexOf('=');
  if (splitIndex >= 0) {
    return {
      value: argument.slice(splitIndex + 1),
      nextIndex: index
    };
  }

  if (index + 1 >= argv.length || argv[index + 1].startsWith('-')) {
    throw new Error(`Missing value for ${optionName}.`);
  }

  return {
    value: argv[index + 1],
    nextIndex: index + 1
  };
}

function formatList(values) {
  return values.length > 0
    ? values.map((value) => `\`${value}\``).join(', ')
    : 'none';
}

function countBy(items, selector) {
  const counts = new Map();
  for (const item of items) {
    const key = selector(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return Object.fromEntries([...counts.entries()].sort(([a], [b]) => a.localeCompare(b)));
}

export {
  countBy,
  defaultCoverageRoot,
  defaultManifestPath,
  defaultOutputRoot,
  defaultSchemaPath,
  defaultSuiteRoot,
  fileExists,
  formatList,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  readManifestWithGenerated,
  repositoryRoot,
  resolveFrom,
  toRepositoryRelative,
  writeJson,
  writeText
};
