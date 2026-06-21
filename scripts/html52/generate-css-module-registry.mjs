#!/usr/bin/env node
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  defaultSuiteRoot,
  normalizePathForDisplay,
  parseRepeatedOption,
  readJson,
  writeJson
} from './common.mjs';

const defaultSourceUrl = 'https://www.w3.org/Style/CSS/current-work.en.html';
const defaultOutputPath = path.join(defaultSuiteRoot, 'generated', 'css-modules', 'registry.json');
const activeStatuses = new Set(['REC', 'CRD', 'CR', 'WD', 'FPWD']);
const excludedIds = new Set(['beijing']);
const snapshotIdPattern = /^css-\d{4}$/;

async function main(argv = process.argv.slice(2)) {
  try {
    const options = parseArguments(argv);
    if (options.help) {
      console.log(getHelpText());
      return 0;
    }

    const html = options.inputPath
      ? await fs.readFile(options.inputPath, 'utf8')
      : await fetchSource(options.sourceUrl);
    options.snapshotDate = await resolveSnapshotDate(options);
    const registry = createRegistry(html, options);

    if (options.check) {
      const existing = await readJson(options.outputPath);
      const expected = JSON.stringify(registry, null, 2);
      const current = JSON.stringify(existing, null, 2);
      if (current !== expected) {
        console.error(`${normalizePathForDisplay(options.outputPath)} is out of date. Run npm run html52:css-registry.`);
        return 1;
      }
      console.log(`${normalizePathForDisplay(options.outputPath)} is up to date.`);
      return 0;
    }

    await writeJson(options.outputPath, registry);
    console.log(`Wrote ${registry.modules.length} active CSS module rows to ${normalizePathForDisplay(options.outputPath)}.`);
    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

function parseArguments(argv) {
  const options = {
    sourceUrl: defaultSourceUrl,
    inputPath: null,
    outputPath: defaultOutputPath,
    snapshotDate: null,
    snapshotDateProvided: false,
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
      case '--source-url': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.sourceUrl = parsed.value;
        index = parsed.nextIndex;
        break;
      }
      case '--input': {
        const parsed = parseRepeatedOption(argv, name, index);
        options.inputPath = path.resolve(parsed.value);
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
      case '--snapshot-date': {
        const parsed = parseRepeatedOption(argv, name, index);
        if (!/^\d{4}-\d{2}-\d{2}$/.test(parsed.value)) {
          throw new Error('--snapshot-date must use YYYY-MM-DD format.');
        }
        options.snapshotDate = parsed.value;
        options.snapshotDateProvided = true;
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

  options.outputPath = path.resolve(options.outputPath);
  return options;
}

async function resolveSnapshotDate(options) {
  if (options.snapshotDateProvided) {
    return options.snapshotDate;
  }
  if (options.check) {
    try {
      const existing = await readJson(options.outputPath);
      const existingSnapshotDate = existing.generatedFrom?.snapshotDate;
      if (typeof existingSnapshotDate === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(existingSnapshotDate)) {
        return existingSnapshotDate;
      }
    } catch {
      // Fall through to today's date so the real check error remains about contents.
    }
  }
  return new Date().toISOString().slice(0, 10);
}

async function fetchSource(sourceUrl) {
  const response = await fetch(sourceUrl, {
    headers: {
      'user-agent': 'Broiler.HTML css-module-registry-generator'
    }
  });
  if (!response.ok) {
    throw new Error(`Failed to fetch ${sourceUrl}: ${response.status} ${response.statusText}`);
  }
  return await response.text();
}

function createRegistry(html, options) {
  const rows = parseCurrentWorkRows(html);
  const activeRows = rows.filter((row) => isActiveModuleRow(row));
  if (activeRows.length < 100) {
    throw new Error(`Expected at least 100 active CSS module rows, found ${activeRows.length}.`);
  }

  return {
    schemaVersion: 1,
    generatedFrom: {
      title: 'W3C CSS current work',
      url: options.sourceUrl,
      snapshotDate: options.snapshotDate,
      activeStatuses: [...activeStatuses]
    },
    counts: {
      totalRows: rows.length,
      activeRows: activeRows.length,
      byCurrentStatus: countBy(activeRows, (row) => row.currentStatus)
    },
    modules: activeRows.map((row) => ({
      id: row.id,
      title: row.title,
      currentStatus: row.currentStatus,
      upcomingStatus: row.upcomingStatus || null,
      url: absolutizeUrl(row.url, options.sourceUrl),
      editorDraftUrl: row.editorDraftUrl ? absolutizeUrl(row.editorDraftUrl, options.sourceUrl) : null,
      notes: row.notes || null
    }))
  };
}

function parseCurrentWorkRows(html) {
  const rows = [];
  const rowMatches = html.matchAll(/<tr\b([^>]*)>([\s\S]*?)(?=<tr\b|<\/table>)/gi);
  for (const rowMatch of rowMatches) {
    const id = readHtmlAttribute(rowMatch[1], 'id');
    if (!id) {
      continue;
    }

    const cells = [...rowMatch[2].matchAll(/<td\b([^>]*)>([\s\S]*?)(?=<td\b|<\/tr>)/gi)].map((cellMatch) => ({
      attributes: cellMatch[1],
      html: cellMatch[2],
      text: normalizeText(cellMatch[2]),
      firstHref: readFirstHref(cellMatch[2])
    }));
    if (cells.length < 3) {
      continue;
    }

    rows.push({
      id,
      title: cells[0].text,
      currentStatus: cells[1].text,
      upcomingStatus: cells[2].text,
      notes: cells[3]?.text ?? '',
      url: cells[0].firstHref,
      editorDraftUrl: cells[2].firstHref
    });
  }
  return rows;
}

function isActiveModuleRow(row) {
  return activeStatuses.has(row.currentStatus)
    && !snapshotIdPattern.test(row.id)
    && !excludedIds.has(row.id);
}

function readHtmlAttribute(attributeText, name) {
  const quoted = attributeText.match(new RegExp(`${name}=(["'])(.*?)\\1`, 'i'));
  if (quoted) {
    return normalizeText(quoted[2]);
  }
  const unquoted = attributeText.match(new RegExp(`${name}=([^\\s>]+)`, 'i'));
  return unquoted ? normalizeText(unquoted[1].replace(/^["']|["']$/g, '')) : '';
}

function readFirstHref(html) {
  const quoted = html.match(/href=(["'])(.*?)\1/i);
  if (quoted) {
    return decodeEntities(quoted[2]);
  }
  const unquoted = html.match(/href=([^\s>]+)/i);
  return unquoted ? decodeEntities(unquoted[1].replace(/^["']|["']$/g, '')) : '';
}

function normalizeText(html) {
  return decodeEntities(html)
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<style[\s\S]*?<\/style>/gi, '')
    .replace(/<[^>]+>/g, ' ')
    .replace(/[\u00a0\u2010-\u2015]/g, ' ')
    .replace(/[\u201c\u201d]/g, '"')
    .replace(/[\u2018\u2019]/g, "'")
    .replace(/\s+/g, ' ')
    .trim();
}

function decodeEntities(value) {
  return value
    .replace(/&nbsp;|&#xA0;|&#160;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#(\d+);/g, (_, decimal) => String.fromCodePoint(Number(decimal)))
    .replace(/&#x([0-9a-f]+);/gi, (_, hexadecimal) => String.fromCodePoint(Number.parseInt(hexadecimal, 16)));
}

function absolutizeUrl(value, baseUrl) {
  return new URL(value, baseUrl).href;
}

function countBy(items, selector) {
  const counts = new Map();
  for (const item of items) {
    const key = selector(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return Object.fromEntries([...counts.entries()].sort(([left], [right]) => left.localeCompare(right)));
}

function getHelpText() {
  return `Usage:
  npm run html52:css-registry -- [options]

Options:
  --source-url <url>       CSS current work page. Defaults to ${defaultSourceUrl}.
  --input <path>           Parse an already-downloaded current-work HTML file.
  --output <path>          Registry output. Defaults to tests/html52/generated/css-modules/registry.json.
  --snapshot-date <date>   Date to write into the registry, YYYY-MM-DD. Defaults to today.
  --check                  Exit non-zero if the output file is out of date.
  -h, --help               Show this help text.`;
}

export {
  createRegistry,
  main,
  parseArguments,
  parseCurrentWorkRows
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main();
}
