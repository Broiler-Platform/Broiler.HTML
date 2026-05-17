#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { promises as fs } from 'node:fs';
import path from 'node:path';

const options = parseArguments(process.argv.slice(2));
if (options.help) {
  console.log(getHelpText());
  process.exit(0);
}

if (!options.output) {
  console.error('Missing required --output argument.');
  console.error();
  console.error(getHelpText());
  process.exit(1);
}

options.output = path.resolve(options.output);

try {
  if (await directoryExists(options.output)) {
    if (!options.force) {
      console.log(`Using existing prepared WPT tree at ${options.output}`);
      process.exit(0);
    }

    await fs.rm(options.output, { recursive: true, force: true });
  }

  if (options.source) {
    const source = path.resolve(options.source);
    const stat = await fs.stat(source).catch(() => null);
    if (!stat?.isDirectory()) {
      throw new Error(`Source WPT directory not found: ${source}`);
    }

    await fs.cp(source, options.output, { recursive: true });
    await writeMetadata(options.output, {
      source,
      mode: 'copy'
    });
    console.log(`Copied WPT tree from ${source} to ${options.output}`);
    process.exit(0);
  }

  const cloneArgs = ['clone', '--depth', '1'];
  if (options.ref) {
    cloneArgs.push('--branch', options.ref);
  }

  cloneArgs.push(options.repo, options.output);
  runCommand('git', cloneArgs, 'Clone WPT repository');
  await writeMetadata(options.output, {
    source: options.repo,
    ref: options.ref,
    mode: 'git-clone'
  });

  console.log(`Prepared WPT tree at ${options.output}`);
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
}

function parseArguments(args) {
  const options = {
    help: false,
    output: null,
    source: null,
    repo: 'https://github.com/web-platform-tests/wpt.git',
    ref: 'main',
    force: false
  };

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    switch (argument) {
      case '--help':
      case '-h':
        options.help = true;
        break;
      case '--output':
        options.output = readValue(args, ++index, argument);
        break;
      case '--source':
        options.source = readValue(args, ++index, argument);
        break;
      case '--repo':
        options.repo = readValue(args, ++index, argument);
        break;
      case '--ref':
        options.ref = readValue(args, ++index, argument);
        break;
      case '--force':
        options.force = true;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  return options;
}

function readValue(args, index, argumentName) {
  const value = args[index];
  if (!value || value.startsWith('-')) {
    throw new Error(`Missing value for ${argumentName}.`);
  }

  return value;
}

function runCommand(command, args, description) {
  const result = spawnSync(command, args, {
    encoding: 'utf8',
    stdio: 'pipe',
    maxBuffer: 10 * 1024 * 1024,
    env: process.env
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    throw new Error([
      `${description} failed with exit code ${result.status ?? 'unknown'}.`,
      result.stdout?.trim() ? `STDOUT:\n${result.stdout.trim()}` : null,
      result.stderr?.trim() ? `STDERR:\n${result.stderr.trim()}` : null
    ].filter(Boolean).join('\n\n'));
  }
}

async function directoryExists(targetPath) {
  const stat = await fs.stat(targetPath).catch(() => null);
  return Boolean(stat?.isDirectory());
}

async function writeMetadata(outputRoot, details) {
  const metadata = {
    preparedAt: new Date().toISOString(),
    ...details
  };

  await fs.writeFile(
    path.join(outputRoot, '.broiler-wpt-prepare.json'),
    `${JSON.stringify(metadata, null, 2)}\n`
  );
}

function getHelpText() {
  return `Usage:
  npm run wpt:prepare -- --output /path/to/wpt [options]

Options:
  --output <dir>        Destination directory for the prepared WPT tree.
  --source <dir>        Copy an existing local WPT tree instead of cloning from GitHub.
  --repo <url>          Git repository URL to clone. Defaults to the official WPT repository.
  --ref <name>          Git ref/branch to clone. Defaults to main.
  --force               Replace the output directory if it already exists.
  -h, --help            Show help.

Examples:
  npm run wpt:prepare -- --output ./artifacts/wpt-source
  npm run wpt:prepare -- --output ./artifacts/wpt-source --source /tmp/wpt-smoke --force`;
}
