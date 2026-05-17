#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import { createReadStream } from 'node:fs';
import { promises as fs } from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');
const toolProjectPath = path.join(repositoryRoot, 'Source', 'Broiler.HTML.Tool', 'Broiler.HTML.Tool.csproj');
const skippedDirectoryNames = new Set(['.git', 'node_modules', 'resources', 'support', 'reference']);
const defaultTestTimeoutMs = 30_000;
const testTimeoutEnvironmentVariableName = 'BROILER_WPT_TEST_TIMEOUT_MS';
const contentTypes = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.gif', 'image/gif'],
  ['.htm', 'text/html; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8'],
  ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.otf', 'font/otf'],
  ['.png', 'image/png'],
  ['.svg', 'image/svg+xml'],
  ['.ttf', 'font/ttf'],
  ['.txt', 'text/plain; charset=utf-8'],
  ['.woff', 'font/woff'],
  ['.woff2', 'font/woff2'],
  ['.xht', 'application/xhtml+xml; charset=utf-8'],
  ['.xhtml', 'application/xhtml+xml; charset=utf-8'],
  ['.xml', 'application/xml; charset=utf-8']
]);

async function main(argv = process.argv.slice(2)) {
  const options = parseArguments(argv);
  if (options.help) {
    console.log(getHelpText());
    return 0;
  }

  if (!options.wptRoot) {
    console.error('Missing required --wpt-root argument.');
    console.error();
    console.error(getHelpText());
    return 1;
  }

  options.wptRoot = path.resolve(options.wptRoot);
  options.output = path.resolve(options.output ?? path.join(repositoryRoot, 'artifacts', 'wpt'));

  try {
    const wptRootStat = await fs.stat(options.wptRoot);
    if (!wptRootStat.isDirectory()) {
      throw new Error(`WPT root is not a directory: ${options.wptRoot}`);
    }

    await fs.mkdir(options.output, { recursive: true });

    const autoAhemPath = path.join(options.wptRoot, 'fonts', 'Ahem.ttf');
    if (await fileExists(autoAhemPath) && !options.fonts.some((font) => font === autoAhemPath || font.startsWith('Ahem='))) {
      options.fonts.unshift(`Ahem=${autoAhemPath}`);
    }

    console.log(`Scanning ${options.wptRoot} for non-JS WPT candidates...`);
    const scanResult = await collectCandidates(options.wptRoot, options.includes, options.limit);
    console.log(`Selected ${scanResult.tests.length} candidate(s); skipped ${scanResult.skippedForJavaScript.length} JS-dependent file(s).`);

    if (scanResult.tests.length === 0) {
      throw new Error('No non-JS WPT files matched the current filters.');
    }

    console.log('Building Broiler.HTML.Tool once before the batch run.');
    runCommand('dotnet', ['build', toolProjectPath, '-nologo'], { description: 'Build Broiler.HTML.Tool' });

    const server = await startStaticServer(options.wptRoot);
    console.log(`Serving WPT files from ${server.baseUrl}`);

    const { chromium } = await import('playwright');
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: { width: options.width, height: options.height },
      javaScriptEnabled: false,
      deviceScaleFactor: 1,
      colorScheme: 'light'
    });

    const failures = [];
    const passes = [];

    try {
      for (const test of scanResult.tests) {
        const caseDirectory = path.join(options.output, 'cases', test.relativePath);
        await fs.mkdir(caseDirectory, { recursive: true });

        const broilerImagePath = path.join(caseDirectory, 'broiler.png');
        const chromiumImagePath = path.join(caseDirectory, 'chromium.png');
        const diffImagePath = path.join(caseDirectory, 'diff.png');
        const reportPath = path.join(caseDirectory, 'report.json');
        const testUrl = `${server.baseUrl}/${test.relativePath}`;
        const testDeadline = Date.now() + options.testTimeoutMs;
        const testStartedAt = Date.now();
        const timings = {};
        let timeoutPhase = null;

        console.log(`Running ${test.relativePath}`);
        try {
          timeoutPhase = 'broiler-render';
          const broilerStartedAt = Date.now();
          await renderWithBroiler(test.fullPath, broilerImagePath, testUrl, options, testDeadline);
          timings.broilerRenderMs = Date.now() - broilerStartedAt;

          timeoutPhase = 'chromium-reference';
          const chromiumStartedAt = Date.now();
          await renderWithChromium(context, test.relativePath, testUrl, chromiumImagePath, testDeadline, options.testTimeoutMs);
          timings.chromiumReferenceMs = Date.now() - chromiumStartedAt;

          timeoutPhase = 'image-compare';
          const compareStartedAt = Date.now();
          const compareResult = runCommand(
            'dotnet',
            [
              'run',
              '--no-build',
              '--project', toolProjectPath,
              '--',
              'compare',
              '--actual', broilerImagePath,
              '--baseline', chromiumImagePath,
              '--diff-output', diffImagePath,
              '--json-output', reportPath,
              '--pixel-diff-threshold', String(options.pixelDiffThreshold),
              '--color-tolerance', String(options.colorTolerance)
            ],
            {
              description: `Compare ${test.relativePath}`,
              allowExitCodes: [0, 1],
              timeoutMs: getRemainingTimeoutMs(testDeadline, options.testTimeoutMs, `Compare ${test.relativePath}`),
              timeoutMessageMs: options.testTimeoutMs
            }
          );
          timings.imageCompareMs = Date.now() - compareStartedAt;
          timeoutPhase = null;

          const report = normalizeCompareReport(JSON.parse(await fs.readFile(reportPath, 'utf8')));
          const entry = {
            path: test.relativePath,
            broilerImagePath,
            chromiumImagePath,
            diffImagePath: report.diffOutputPath,
            reportPath,
            diffRatio: report.diffRatio,
            mismatch: report.mismatch,
            timeout: false,
            timeoutPhase: null,
            error: null,
            totalDurationMs: Date.now() - testStartedAt,
            timings
          };

          if (compareResult.status === 0) {
            passes.push(entry);
          } else {
            failures.push(entry);
          }
        } catch (error) {
          if (!isTimeoutError(error)) {
            throw error;
          }

          const message = error instanceof Error ? error.message : String(error);
          console.error(message);
          failures.push(await createTimedOutFailure(test.relativePath, broilerImagePath, chromiumImagePath, message, timeoutPhase, timings, Date.now() - testStartedAt));
        }
      }
    } finally {
      await context.close();
      await browser.close();
      await new Promise((resolve, reject) => server.instance.close((error) => error ? reject(error) : resolve()));
    }

    const summary = {
      generatedAt: new Date().toISOString(),
      wptRoot: options.wptRoot,
      outputRoot: options.output,
      viewport: { width: options.width, height: options.height },
      thresholds: {
        pixelDiffThreshold: options.pixelDiffThreshold,
        colorTolerance: options.colorTolerance
      },
      timeouts: {
        perTestMs: options.testTimeoutMs
      },
      totalCandidates: scanResult.tests.length,
      skippedForJavaScriptCount: scanResult.skippedForJavaScript.length,
      skippedForJavaScript: scanResult.skippedForJavaScript,
      passedCount: passes.length,
      failedCount: failures.length,
      timedOutCount: failures.filter((failure) => failure.timeout).length,
      passed: passes,
      failed: failures
    };

    const summaryJsonPath = path.join(options.output, 'summary.json');
    const summaryMarkdownPath = path.join(options.output, 'summary.md');
    await fs.writeFile(summaryJsonPath, JSON.stringify(summary, null, 2));
    await fs.writeFile(summaryMarkdownPath, createSummaryMarkdown(summary));

    console.log(`Wrote summary to ${summaryJsonPath}`);
    console.log(`Wrote markdown summary to ${summaryMarkdownPath}`);

    if (failures.length > 0) {
      console.error(`${failures.length} test(s) differed from Chromium.`);
      return options.exitZeroOnDifferences ? 0 : 1;
    }

    console.log(`All ${passes.length} rendered test(s) matched Chromium within the configured thresholds.`);
    return 0;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(message);
    return 1;
  }
}

function parseArguments(args, env = process.env) {
  const options = {
    help: false,
    wptRoot: null,
    output: null,
    includes: [],
    limit: 0,
    width: 800,
    height: 600,
    fonts: [],
    pixelDiffThreshold: 0.001,
    colorTolerance: 5,
    testTimeoutMs: readTimeoutFromEnvironment(env),
    exitZeroOnDifferences: false
  };

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    switch (argument) {
      case '--help':
      case '-h':
        options.help = true;
        break;
      case '--wpt-root':
        options.wptRoot = readValue(args, ++index, argument);
        break;
      case '--output':
        options.output = readValue(args, ++index, argument);
        break;
      case '--include':
        options.includes.push(readValue(args, ++index, argument));
        break;
      case '--limit':
        options.limit = readInteger(args, ++index, argument, 0);
        break;
      case '--width':
        options.width = readInteger(args, ++index, argument, 1);
        break;
      case '--height':
        options.height = readInteger(args, ++index, argument, 1);
        break;
      case '--font':
        options.fonts.push(readValue(args, ++index, argument));
        break;
      case '--pixel-diff-threshold':
        options.pixelDiffThreshold = readNumber(args, ++index, argument, 0, 1);
        break;
      case '--color-tolerance':
        options.colorTolerance = readInteger(args, ++index, argument, 0, 255);
        break;
      case '--test-timeout-ms':
        options.testTimeoutMs = readInteger(args, ++index, argument, 1);
        break;
      case '--exit-zero-on-differences':
        options.exitZeroOnDifferences = true;
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

function readTimeoutFromEnvironment(env) {
  const rawValue = env[testTimeoutEnvironmentVariableName];
  if (rawValue === undefined || rawValue === '') {
    return defaultTestTimeoutMs;
  }

  const value = Number.parseInt(rawValue, 10);
  if (!Number.isInteger(value) || value < 1) {
    throw new Error(`Invalid integer value for ${testTimeoutEnvironmentVariableName}: ${rawValue}`);
  }

  return value;
}

function readInteger(args, index, argumentName, min, max = Number.MAX_SAFE_INTEGER) {
  const rawValue = readValue(args, index, argumentName);
  const value = Number.parseInt(rawValue, 10);
  if (!Number.isInteger(value) || value < min || value > max) {
    throw new Error(`Invalid integer value for ${argumentName}: ${rawValue}`);
  }

  return value;
}

function readNumber(args, index, argumentName, min, max) {
  const rawValue = readValue(args, index, argumentName);
  const value = Number.parseFloat(rawValue);
  if (!Number.isFinite(value) || value < min || value > max) {
    throw new Error(`Invalid numeric value for ${argumentName}: ${rawValue}`);
  }

  return value;
}

async function collectCandidates(root, includes, limit) {
  const tests = [];
  const skippedForJavaScript = [];
  await walk('');

  return {
    tests: limit > 0 ? tests.slice(0, limit) : tests,
    skippedForJavaScript
  };

  async function walk(relativeDirectory) {
    const absoluteDirectory = path.join(root, relativeDirectory);
    const entries = await fs.readdir(absoluteDirectory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name));

    for (const entry of entries) {
      const relativePath = relativeDirectory
        ? `${relativeDirectory}/${entry.name}`
        : entry.name;

      if (entry.isDirectory()) {
        if (shouldSkipDirectory(entry.name)) {
          continue;
        }

        await walk(relativePath);
        if (limit > 0 && tests.length >= limit) {
          return;
        }
        continue;
      }

      if (!isCandidateDocument(entry.name, relativePath, includes)) {
        continue;
      }

      const fullPath = path.join(root, ...relativePath.split('/'));
      const markup = await fs.readFile(fullPath, 'utf8');
      if (requiresJavaScript(markup)) {
        skippedForJavaScript.push(relativePath);
        continue;
      }

      tests.push({ relativePath, fullPath });
      if (limit > 0 && tests.length >= limit) {
        return;
      }
    }
  }
}

function shouldSkipDirectory(name) {
  return skippedDirectoryNames.has(name.toLowerCase());
}

function isCandidateDocument(fileName, relativePath, includes) {
  const extension = path.extname(fileName).toLowerCase();
  if (!['.htm', '.html', '.xht', '.xhtml'].includes(extension)) {
    return false;
  }

  const lowerRelativePath = relativePath.toLowerCase();
  if (lowerRelativePath.endsWith('-ref.html') || lowerRelativePath.endsWith('-ref.htm') || lowerRelativePath.endsWith('-ref.xhtml') ||
      lowerRelativePath.endsWith('-notref.html') || lowerRelativePath.endsWith('-notref.htm') || lowerRelativePath.endsWith('-notref.xhtml')) {
    return false;
  }

  if (includes.length === 0) {
    return true;
  }

  return includes.some((include) => lowerRelativePath.includes(include.toLowerCase()));
}

function requiresJavaScript(markup) {
  return /<script\b/i.test(markup) ||
    /\bon[a-z]+\s*=\s*["']/i.test(markup) ||
    /javascript:/i.test(markup) ||
    /testharness\.js|testdriver\.js|reftest-wait/i.test(markup);
}

async function renderWithBroiler(inputPath, outputPath, baseUrl, options, testDeadline) {
  const args = [
    'run',
    '--no-build',
    '--project', toolProjectPath,
    '--',
    '--input', inputPath,
    '--output', outputPath,
    '--base-url', baseUrl,
    '--width', String(options.width),
    '--height', String(options.height)
  ];

  for (const font of options.fonts) {
    args.push('--font', font);
  }

  await runCommandAsync('dotnet', args, {
    description: `Render ${path.basename(inputPath)} with Broiler.HTML`,
    timeoutMs: getRemainingTimeoutMs(testDeadline, options.testTimeoutMs, `Render ${path.basename(inputPath)} with Broiler.HTML`),
    timeoutMessageMs: options.testTimeoutMs
  });
}

async function runCommandAsync(command, args, { description, allowExitCodes = [0], timeoutMs = 0, timeoutMessageMs = timeoutMs } = {}) {
  const child = spawn(command, args, {
    cwd: repositoryRoot,
    env: process.env,
    stdio: ['ignore', 'pipe', 'pipe']
  });

  let stdout = '';
  let stderr = '';
  let timedOut = false;
  const maxBuffer = 10 * 1024 * 1024;

  child.stdout?.setEncoding('utf8');
  child.stdout?.on('data', (chunk) => {
    stdout += chunk;
    if (stdout.length + stderr.length > maxBuffer) {
      child.kill('SIGKILL');
    }
  });

  child.stderr?.setEncoding('utf8');
  child.stderr?.on('data', (chunk) => {
    stderr += chunk;
    if (stdout.length + stderr.length > maxBuffer) {
      child.kill('SIGKILL');
    }
  });

  return await new Promise((resolve, reject) => {
    const timer = timeoutMs > 0
      ? setTimeout(() => {
        timedOut = true;
        child.kill('SIGKILL');
      }, timeoutMs)
      : null;

    child.once('error', (error) => {
      if (timer) {
        clearTimeout(timer);
      }
      reject(error);
    });

    child.once('close', (code) => {
      if (timer) {
        clearTimeout(timer);
      }

      if (timedOut) {
        reject(createTimeoutError(description ?? command, timeoutMessageMs, { stdout, stderr }));
        return;
      }

      if (code === null || !allowExitCodes.includes(code)) {
        reject(new Error([
          `${description ?? command} failed with exit code ${code ?? 'unknown'}.`,
          stdout.trim() ? `STDOUT:\n${stdout.trim()}` : null,
          stderr.trim() ? `STDERR:\n${stderr.trim()}` : null
        ].filter(Boolean).join('\n\n')));
        return;
      }

      resolve({ status: code, stdout, stderr });
    });
  });
}

async function renderWithChromium(context, relativePath, testUrl, outputPath, testDeadline, testTimeoutMs) {
  const page = await context.newPage();
  try {
    try {
      await page.goto(testUrl, {
        waitUntil: 'load',
        timeout: getRemainingTimeoutMs(testDeadline, testTimeoutMs, `Load Chromium reference for ${relativePath}`)
      });
      await page.screenshot({
        path: outputPath,
        animations: 'disabled',
        caret: 'hide',
        timeout: getRemainingTimeoutMs(testDeadline, testTimeoutMs, `Capture Chromium reference for ${relativePath}`)
      });
    } catch (error) {
      if (isTimeoutError(error)) {
        throw createTimeoutError(`Chromium reference for ${relativePath}`, testTimeoutMs);
      }

      throw error;
    }
  } finally {
    await page.close();
  }
}

function runCommand(command, args, { description, allowExitCodes = [0], timeoutMs = 0, timeoutMessageMs = timeoutMs } = {}) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    maxBuffer: 10 * 1024 * 1024,
    stdio: 'pipe',
    env: process.env,
    timeout: timeoutMs,
    killSignal: 'SIGKILL'
  });

  if (isSpawnTimeoutError(result.error)) {
    throw createTimeoutError(description ?? command, timeoutMessageMs, result);
  }

  if (result.error) {
    throw result.error;
  }

  if (!allowExitCodes.includes(result.status ?? 1)) {
    throw new Error([
      `${description ?? command} failed with exit code ${result.status ?? 'unknown'}.`,
      result.stdout?.trim() ? `STDOUT:\n${result.stdout.trim()}` : null,
      result.stderr?.trim() ? `STDERR:\n${result.stderr.trim()}` : null
    ].filter(Boolean).join('\n\n'));
  }

  return result;
}

function getRemainingTimeoutMs(testDeadline, testTimeoutMs, description) {
  const remainingTimeoutMs = testDeadline - Date.now();
  if (remainingTimeoutMs <= 0) {
    throw createTimeoutError(description, testTimeoutMs);
  }

  return remainingTimeoutMs;
}

function isSpawnTimeoutError(error) {
  return error instanceof Error && (error.code === 'ETIMEDOUT' || error.errno === 'ETIMEDOUT');
}

function createTimeoutError(description, timeoutMs, result) {
  const error = new Error([
    `${description} timed out after ${timeoutMs}ms.`,
    result?.stdout?.trim() ? `STDOUT:\n${result.stdout.trim()}` : null,
    result?.stderr?.trim() ? `STDERR:\n${result.stderr.trim()}` : null
  ].filter(Boolean).join('\n\n'));
  error.name = 'TimeoutError';
  return error;
}

function isTimeoutError(error) {
  return error instanceof Error && error.name === 'TimeoutError';
}

async function startStaticServer(root) {
  const safeRootPrefix = `${root}${path.sep}`;
  const server = http.createServer(async (request, response) => {
    try {
      const url = new URL(request.url ?? '/', 'http://127.0.0.1');
      const decodedPath = decodeURIComponent(url.pathname);
      const absolutePath = path.resolve(root, `.${decodedPath}`);
      if (absolutePath !== root && !absolutePath.startsWith(safeRootPrefix)) {
        response.statusCode = 403;
        response.end('Forbidden');
        return;
      }

      const stat = await fs.stat(absolutePath).catch(() => null);
      if (!stat || stat.isDirectory()) {
        response.statusCode = 404;
        response.end('Not found');
        return;
      }

      response.setHeader('Content-Type', contentTypeForPath(absolutePath));
      createReadStream(absolutePath).pipe(response);
    } catch (error) {
      response.statusCode = 500;
      response.end(error instanceof Error ? error.message : String(error));
    }
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });

  const address = server.address();
  if (!address || typeof address === 'string') {
    throw new Error('Failed to start the local WPT file server.');
  }

  return {
    instance: server,
    baseUrl: `http://127.0.0.1:${address.port}`
  };
}

function contentTypeForPath(filePath) {
  return contentTypes.get(path.extname(filePath).toLowerCase()) ?? 'application/octet-stream';
}

function normalizeCompareReport(report) {
  return {
    diffOutputPath: readReportProperty(report, 'diffOutputPath'),
    diffRatio: normalizeDiffRatio(readReportProperty(report, 'diffRatio')),
    mismatch: normalizeMismatch(readReportProperty(report, 'mismatch'))
  };
}

function readReportProperty(report, name) {
  if (!report || typeof report !== 'object') {
    return null;
  }

  const pascalName = `${name.charAt(0).toUpperCase()}${name.slice(1)}`;
  return report[name] ?? report[pascalName] ?? null;
}

function normalizeMismatch(mismatch) {
  if (!mismatch || typeof mismatch !== 'object') {
    return null;
  }

  return {
    category: readReportProperty(mismatch, 'category'),
    summary: readReportProperty(mismatch, 'summary'),
    averageChannelDelta: normalizeFiniteNumber(readReportProperty(mismatch, 'averageChannelDelta')),
    maxChannelDelta: normalizeFiniteNumber(readReportProperty(mismatch, 'maxChannelDelta')),
    affectedRows: normalizeFiniteNumber(readReportProperty(mismatch, 'affectedRows')),
    affectedColumns: normalizeFiniteNumber(readReportProperty(mismatch, 'affectedColumns'))
  };
}

function normalizeDiffRatio(value) {
  return normalizeFiniteNumber(value);
}

function normalizeFiniteNumber(value) {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }

  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function formatDiffRatio(value) {
  const normalized = normalizeDiffRatio(value);
  return normalized === null ? 'n/a' : normalized.toFixed(6);
}

function createSummaryMarkdown(summary) {
  const lines = [
    '# Broiler.HTML non-JS WPT summary',
    '',
    `- WPT root: \`${summary.wptRoot}\``,
    `- Output root: \`${summary.outputRoot}\``,
    `- Viewport: ${summary.viewport.width}x${summary.viewport.height}`,
    `- Pixel diff threshold: ${summary.thresholds.pixelDiffThreshold}`,
    `- Color tolerance: ${summary.thresholds.colorTolerance}`,
    `- Per-test timeout: ${summary.timeouts?.perTestMs ?? defaultTestTimeoutMs} ms`,
    `- Total candidates: ${summary.totalCandidates}`,
    `- Passed: ${summary.passedCount}`,
    `- Failed: ${summary.failedCount}`,
    `- Timed out: ${summary.timedOutCount ?? 0}`,
    `- Skipped for JavaScript: ${summary.skippedForJavaScriptCount}`,
    ''
  ];

  if (summary.failed.length > 0) {
    lines.push('## Failures', '', '| Test | Diff ratio | Result | Report | Diff |', '| --- | ---: | --- | --- | --- |');
    for (const failure of summary.failed) {
      lines.push(`| \`${failure.path}\` | ${formatDiffRatio(failure.diffRatio)} | ${describeFailure(failure)} | ${failure.reportPath ? `\`${failure.reportPath}\`` : 'n/a'} | ${failure.diffImagePath ? `\`${failure.diffImagePath}\`` : 'n/a'} |`);
    }
    lines.push('');
  }

  if (summary.skippedForJavaScript.length > 0) {
    lines.push('## Skipped for JavaScript', '');
    for (const skipped of summary.skippedForJavaScript) {
      lines.push(`- \`${skipped}\``);
    }
    lines.push('');
  }

  return `${lines.join('\n')}\n`;
}

function describeFailure(failure) {
  return failure.timeout ? failure.error ?? 'Timed out' : failure.mismatch?.category ?? 'n/a';
}

async function fileExists(filePath) {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

async function createTimedOutFailure(relativePath, broilerImagePath, chromiumImagePath, message, timeoutPhase, timings, totalDurationMs) {
  const [hasBroilerImage, hasChromiumImage] = await Promise.all([
    fileExists(broilerImagePath),
    fileExists(chromiumImagePath)
  ]);

  return {
    path: relativePath,
    broilerImagePath: hasBroilerImage ? broilerImagePath : null,
    chromiumImagePath: hasChromiumImage ? chromiumImagePath : null,
    diffImagePath: null,
    reportPath: null,
    diffRatio: null,
    mismatch: null,
    timeout: true,
    timeoutPhase,
    error: message,
    totalDurationMs,
    timings
  };
}

function getHelpText() {
  return `Usage:
  npm run wpt:run -- --wpt-root /path/to/wpt [options]

Options:
  --wpt-root <dir>               Path to a local web-platform-tests checkout.
  --output <dir>                 Directory for screenshots, diffs, and summaries. Defaults to ./artifacts/wpt.
  --include <substring>          Restrict to relative paths containing the substring. Repeat as needed.
  --limit <count>                Stop after selecting this many candidate files.
  --width <pixels>               Chromium/Broiler viewport width. Defaults to 800.
  --height <pixels>              Chromium/Broiler viewport height. Defaults to 600.
  --font [Alias=]<path>          Register a local font with Broiler before each render. Repeat as needed.
  --pixel-diff-threshold <0-1>   Pixel diff pass threshold. Defaults to 0.001.
  --color-tolerance <0-255>      Per-channel diff tolerance. Defaults to 5.
  --test-timeout-ms <ms>         Per-test timeout in milliseconds. Defaults to ${defaultTestTimeoutMs}; can also be set via ${testTimeoutEnvironmentVariableName}.
  --exit-zero-on-differences     Keep exit code 0 when visual mismatches are found.
  -h, --help                     Show help.

Notes:
  - Chromium is launched through Playwright with JavaScript disabled.
  - The runner skips files that appear to depend on JavaScript (script tags, inline handlers, javascript: URLs, or common WPT JS harness markers).
  - Each selected WPT case shares a single timeout budget across Broiler rendering, Chromium capture, and image comparison.
  - Relative assets are served from a temporary local HTTP server so Chromium and Broiler resolve them from the same base URL.`;
}

export { createSummaryMarkdown, formatDiffRatio, main, normalizeCompareReport, normalizeDiffRatio, parseArguments, runCommand, runCommandAsync };

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
