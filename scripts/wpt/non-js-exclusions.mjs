import path from 'node:path';
import { promises as fs } from 'node:fs';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const defaultExclusionManifestPath = path.join(scriptDirectory, 'non-js-exclusions.json');

async function readNonJsExclusionManifest(manifestPath = defaultExclusionManifestPath) {
  const resolvedPath = path.resolve(manifestPath);
  const manifest = JSON.parse(await fs.readFile(resolvedPath, 'utf8'));
  if (!Array.isArray(manifest)) {
    throw new Error(`WPT exclusion manifest must contain an array: ${resolvedPath}`);
  }

  const seenPaths = new Set();
  return manifest.map((entry, index) => {
    if (!entry || typeof entry !== 'object') {
      throw new Error(`WPT exclusion manifest entry #${index + 1} must be an object: ${resolvedPath}`);
    }

    const normalized = {
      path: readStringProperty(entry, 'path', resolvedPath, index),
      category: readStringProperty(entry, 'category', resolvedPath, index),
      feature: readStringProperty(entry, 'feature', resolvedPath, index),
      note: readStringProperty(entry, 'note', resolvedPath, index)
    };

    if (seenPaths.has(normalized.path)) {
      throw new Error(`Duplicate WPT exclusion path in ${resolvedPath}: ${normalized.path}`);
    }

    seenPaths.add(normalized.path);
    return normalized;
  });
}

function createNonJsExclusionTableMarkdown(exclusions) {
  const lines = [
    '| Test path | Category | Feature / aspect | Reason for exclusion |',
    '| --- | --- | --- | --- |'
  ];

  for (const exclusion of exclusions) {
    lines.push(`| \`${exclusion.path}\` | ${exclusion.category} | ${escapeMarkdownCell(exclusion.feature)} | ${escapeMarkdownCell(exclusion.note)} |`);
  }

  return lines.join('\n');
}

function readStringProperty(entry, name, manifestPath, index) {
  const value = entry[name];
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`WPT exclusion manifest entry #${index + 1} is missing a non-empty "${name}" string: ${manifestPath}`);
  }

  return value;
}

function escapeMarkdownCell(value) {
  return value.replace(/\|/g, '\\|');
}

export { createNonJsExclusionTableMarkdown, defaultExclusionManifestPath, readNonJsExclusionManifest };
