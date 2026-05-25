import assert from 'node:assert/strict';
import os from 'node:os';
import path from 'node:path';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import http from 'node:http';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { createNonJsExclusionTableMarkdown, readNonJsExclusionManifest } from './non-js-exclusions.mjs';
import {
  collectCandidates,
  createSummaryMarkdown,
  formatDiffRatio,
  getChromiumScreenshotOptions,
  hasInlineCssAnimations,
  normalizeCompareReport,
  normalizeDiffRatio,
  parseArguments,
  runCommand,
  runCommandAsync
} from './run-non-js.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');

test('normalizeDiffRatio accepts finite numbers and numeric strings', () => {
  assert.equal(normalizeDiffRatio(0.25), 0.25);
  assert.equal(normalizeDiffRatio('0.5'), 0.5);
});

test('normalizeDiffRatio rejects missing and invalid values', () => {
  assert.equal(normalizeDiffRatio(undefined), null);
  assert.equal(normalizeDiffRatio(null), null);
  assert.equal(normalizeDiffRatio(''), null);
  assert.equal(normalizeDiffRatio('nope'), null);
  assert.equal(normalizeDiffRatio(Number.NaN), null);
});

test('formatDiffRatio prints n/a for missing values', () => {
  assert.equal(formatDiffRatio(undefined), 'n/a');
  assert.equal(formatDiffRatio(''), 'n/a');
});

test('createSummaryMarkdown tolerates failures without diff ratios', () => {
  const markdown = createSummaryMarkdown({
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    thresholds: { pixelDiffThreshold: 0.001, colorTolerance: 5 },
    timeouts: { perTestMs: 30000 },
    totalCandidates: 1,
    passedCount: 0,
    failedCount: 1,
    timedOutCount: 0,
    skippedForJavaScriptCount: 0,
    skippedForJavaScript: [],
    failed: [
      {
        path: 'css/css-backgrounds/example.html',
        diffRatio: undefined,
        mismatch: null,
        reportPath: '/tmp/out/report.json',
        diffImagePath: null,
        timeout: false,
        error: null
      }
    ]
  });

  assert.match(markdown, /\| `css\/css-backgrounds\/example\.html` \| n\/a \| n\/a \| `\/tmp\/out\/report\.json` \| n\/a \|/);
});

test('parseArguments reads timeout from the environment and lets CLI arguments override it', () => {
  const env = { BROILER_WPT_TEST_TIMEOUT_MS: '45000' };

  assert.equal(parseArguments([], env).testTimeoutMs, 45000);
  assert.equal(parseArguments(['--test-timeout-ms', '12000'], env).testTimeoutMs, 12000);
});

test('parseArguments collects repeated include and exclude filters', () => {
  const options = parseArguments([
    '--include', 'css/css-backgrounds',
    '--include', 'css/css-text',
    '--exclude', 'background-attachment-fixed',
    '--exclude', 'background_repeat_space',
    '--exclude-manifest', './scripts/wpt/non-js-exclusions.json',
    '--scan-only'
  ]);

  assert.deepEqual(options.includes, ['css/css-backgrounds', 'css/css-text']);
  assert.deepEqual(options.excludes, ['background-attachment-fixed', 'background_repeat_space']);
  assert.equal(options.excludeManifest, './scripts/wpt/non-js-exclusions.json');
  assert.equal(options.scanOnly, true);
});

test('parseArguments rejects invalid timeout and threshold values', () => {
  assert.throws(
    () => parseArguments([], { BROILER_WPT_TEST_TIMEOUT_MS: '0' }),
    /Invalid integer value for BROILER_WPT_TEST_TIMEOUT_MS: 0/
  );
  assert.throws(
    () => parseArguments(['--pixel-diff-threshold', '1.1']),
    /Invalid numeric value for --pixel-diff-threshold: 1.1/
  );
  assert.throws(
    () => parseArguments(['--color-tolerance', '256']),
    /Invalid integer value for --color-tolerance: 256/
  );
});

test('runCommand reports timed out child processes clearly', () => {
  assert.throws(
    () => runCommand(process.execPath, ['-e', 'setTimeout(() => {}, 200)'], {
      description: 'Synthetic timeout test',
      timeoutMs: 25,
      timeoutMessageMs: 25
    }),
    /Synthetic timeout test timed out after 25ms\./
  );
});

test('runCommandAsync keeps in-process HTTP servers responsive', async () => {
  const server = http.createServer((request, response) => {
    response.statusCode = 200;
    response.end('ok');
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  assert.ok(address && typeof address !== 'string');

  try {
    const result = await runCommandAsync(process.execPath, [
      '-e',
      `fetch('http://127.0.0.1:${address.port}/').then(async (response) => {
        const body = await response.text();
        if (!response.ok || body !== 'ok') {
          process.exit(2);
        }
        process.exit(0);
      }).catch((error) => {
        console.error(error);
        process.exit(1);
      });`
    ], {
      description: 'Synthetic async HTTP fetch test',
      timeoutMs: 5000,
      timeoutMessageMs: 5000
    });

    assert.equal(result.status, 0);
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
});

test('normalizeCompareReport accepts PascalCase compare reports from the .NET CLI', () => {
  assert.deepEqual(
    normalizeCompareReport({
      DiffOutputPath: '/tmp/out/diff.png',
      DiffRatio: 0.07416666666666667,
      Mismatch: {
        Category: 'MissingContent',
        Summary: '10000/10000 sampled mismatches are background↔content transitions, indicating missing or extra elements.',
        AverageChannelDelta: 127.5,
        MaxChannelDelta: 255,
        AffectedRows: 50,
        AffectedColumns: 200
      }
    }),
    {
      diffOutputPath: '/tmp/out/diff.png',
      diffRatio: 0.07416666666666667,
      mismatch: {
        category: 'MissingContent',
        summary: '10000/10000 sampled mismatches are background↔content transitions, indicating missing or extra elements.',
        averageChannelDelta: 127.5,
        maxChannelDelta: 255,
        affectedRows: 50,
        affectedColumns: 200
      }
    }
  );
});

test('createSummaryMarkdown labels timed out cases explicitly', () => {
  const markdown = createSummaryMarkdown({
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    thresholds: { pixelDiffThreshold: 0.001, colorTolerance: 5 },
    timeouts: { perTestMs: 30000 },
    totalCandidates: 1,
    passedCount: 0,
    failedCount: 1,
    timedOutCount: 1,
    skippedForJavaScriptCount: 0,
    skippedForJavaScript: [],
    failed: [
      {
        path: 'css/css-backgrounds/hang.html',
        diffRatio: null,
        mismatch: null,
        reportPath: null,
        diffImagePath: null,
        timeout: true,
        error: 'Compare css/css-backgrounds/hang.html timed out after 30000ms.'
      }
    ]
  });

  assert.match(markdown, /- Per-test timeout: 30000 ms/);
  assert.match(markdown, /- Timed out: 1/);
  assert.match(markdown, /\| `css\/css-backgrounds\/hang\.html` \| n\/a \| Compare css\/css-backgrounds\/hang\.html timed out after 30000ms\. \| n\/a \| n\/a \|/);
});

test('createSummaryMarkdown truncates large JavaScript skip lists in markdown output', () => {
  const skippedForJavaScript = Array.from({ length: 55 }, (_, index) => `css/case-${index + 1}.html`);
  const markdown = createSummaryMarkdown({
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    thresholds: { pixelDiffThreshold: 0.001, colorTolerance: 5 },
    timeouts: { perTestMs: 30000 },
    totalCandidates: 1,
    passedCount: 1,
    failedCount: 0,
    timedOutCount: 0,
    skippedForJavaScriptCount: skippedForJavaScript.length,
    skippedForJavaScript,
    failed: []
  });

  assert.match(markdown, /css\/case-50\.html/);
  assert.doesNotMatch(markdown, /css\/case-55\.html/);
  assert.match(markdown, /\.\.\. 5 more file\(s\) omitted from markdown; see `summary\.json` for the full list\./);
});

test('collectCandidates respects exclude filters after include matching', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-candidates-'));
  await mkdir(path.join(root, 'css', 'css-backgrounds'), { recursive: true });
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'keep.html'), '<!doctype html><p>keep</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'skip.html'), '<!doctype html><p>skip</p>');

  const result = await collectCandidates(root, ['css/css-backgrounds'], ['skip.html'], 0);

  assert.deepEqual(result.tests.map((testCase) => testCase.relativePath), ['css/css-backgrounds/keep.html']);
});

test('collectCandidates skips support files, reference variants, and JS-dependent documents', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-candidates-'));
  await mkdir(path.join(root, 'css', 'css-backgrounds', 'support'), { recursive: true });
  await mkdir(path.join(root, 'css', 'reference'), { recursive: true });
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'keep.html'), '<!doctype html><p>keep</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'case-ref.html'), '<!doctype html><p>reference</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'case-notref.xhtml'), '<!doctype html><p>not reference</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'js-harness.html'), '<script src="/resources/testharness.js"></script>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'support', 'helper.html'), '<!doctype html><p>helper</p>');
  await writeFile(path.join(root, 'css', 'reference', 'baseline.html'), '<!doctype html><p>baseline</p>');

  const result = await collectCandidates(root, [], [], 0);

  assert.deepEqual(result.tests.map((testCase) => testCase.relativePath), ['css/css-backgrounds/keep.html']);
  assert.deepEqual(result.skippedForJavaScript, ['css/css-backgrounds/js-harness.html']);
});

test('collectCandidates flags inline CSS animation cases', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-candidates-'));
  await mkdir(path.join(root, 'css', 'css-backgrounds'), { recursive: true });
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'animated.html'), `
    <!doctype html>
    <style>
      @keyframes pulse { from { opacity: 0; } to { opacity: 1; } }
      div { animation: pulse 100s; }
    </style>
    <div>animated</div>
  `);
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'static.html'), '<!doctype html><div>static</div>');

  const result = await collectCandidates(root, [], [], 0);

  assert.deepEqual(
    result.tests.map((testCase) => ({
      relativePath: testCase.relativePath,
      usesCssAnimations: testCase.usesCssAnimations
    })),
    [
      { relativePath: 'css/css-backgrounds/animated.html', usesCssAnimations: true },
      { relativePath: 'css/css-backgrounds/static.html', usesCssAnimations: false }
    ]
  );
});

test('hasInlineCssAnimations detects animation declarations and keyframes', () => {
  assert.equal(hasInlineCssAnimations('<style>@keyframes fade { from { opacity: 0; } to { opacity: 1; } }</style>'), true);
  assert.equal(hasInlineCssAnimations('<style>.box { animation-duration: 100s; }</style>'), true);
  assert.equal(hasInlineCssAnimations('<style>.box { color: red; }</style>'), false);
});

test('getChromiumScreenshotOptions keeps CSS animation references live', () => {
  const deadline = Date.now() + 5_000;
  const animationOptions = getChromiumScreenshotOptions(
    { relativePath: 'css/css-backgrounds/animations/case.html', usesCssAnimations: true },
    '/tmp/animated.png',
    deadline,
    5_000
  );
  const staticOptions = getChromiumScreenshotOptions(
    { relativePath: 'css/css-backgrounds/case.html', usesCssAnimations: false },
    '/tmp/static.png',
    deadline,
    5_000
  );

  assert.equal(animationOptions.path, '/tmp/animated.png');
  assert.equal(animationOptions.caret, 'hide');
  assert.ok(!('animations' in animationOptions));
  assert.equal(staticOptions.animations, 'disabled');
});

test('non-JS WPT exclusion manifest contains unique documented paths', async () => {
  const exclusions = await readNonJsExclusionManifest(path.join(repositoryRoot, 'scripts', 'wpt', 'non-js-exclusions.json'));

  assert.equal(exclusions.length, 83);
  assert.equal(new Set(exclusions.map((exclusion) => exclusion.path)).size, exclusions.length);
  assert.ok(exclusions.every((exclusion) => ['unsupported', 'unstable'].includes(exclusion.category)));
  assert.ok(exclusions.some((exclusion) => exclusion.path === 'css/css-backgrounds/background-334.html' && exclusion.category === 'unstable'));
  assert.ok(exclusions.some((exclusion) => exclusion.path === 'css/css-backgrounds/background-clip/clip-text-transform.html' && exclusion.feature === 'background-clip:text'));
});

test('non-JS WPT workflow inventories the full non-JS corpus and applies the documented exclusion manifest', async () => {
  const workflow = await readFile(path.join(repositoryRoot, '.github', 'workflows', 'wpt-non-js.yml'), 'utf8');

  assert.match(workflow, /Inventory all non-JS WPT tests/);
  assert.match(workflow, /--exclude-manifest \.\/scripts\/wpt\/non-js-exclusions\.json/);
  assert.match(workflow, /--scan-only/);
  assert.match(workflow, /Run focused non-JS WPT render\/diff batch/);
});

test('documentation contains the rendered non-JS WPT exclusion table', async () => {
  const exclusions = await readNonJsExclusionManifest(path.join(repositoryRoot, 'scripts', 'wpt', 'non-js-exclusions.json'));
  const compliance = await readFile(path.join(repositoryRoot, 'docs', 'compliance.md'), 'utf8');
  const expectedTable = createNonJsExclusionTableMarkdown(exclusions);

  assert.match(compliance, /<!-- BEGIN: non-js-wpt-exclusions -->[\s\S]*<!-- END: non-js-wpt-exclusions -->/);
  assert.ok(compliance.includes(expectedTable));
});
