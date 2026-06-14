import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
  buildIssueBody,
  buildIssueTitle,
  main,
  parseArguments,
  selectMostCommonFailureGroup
} from './report-common-failure-issue.mjs';

test('selectMostCommonFailureGroup prefers the most frequent timeout phase', () => {
  const group = selectMostCommonFailureGroup({
    failed: [
      { path: 'css/css-backgrounds/a.html', timeout: true, error: 'Render a.html with Broiler.HTML timed out after 30000ms.', totalDurationMs: 30000 },
      { path: 'css/css-backgrounds/b.html', timeout: true, timeoutPhase: 'broiler-render', error: 'Render b.html with Broiler.HTML timed out after 30000ms.', totalDurationMs: 29900 },
      { path: 'css/css-backgrounds/c.html', timeout: false, diffRatio: 0.12, mismatch: { category: 'MissingContent', summary: 'Missing elements.' } }
    ]
  });

  assert.equal(group.signature, 'timeout:broiler-render');
  assert.equal(group.failures.length, 2);
  assert.equal(group.representative.path, 'css/css-backgrounds/a.html');
  assert.equal(group.commonPathPrefix, 'css/css-backgrounds');
  assert.equal(buildIssueTitle(group), 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds');
});

test('buildIssueBody includes diagnostic context and rerun arguments', () => {
  const summary = {
    generatedAt: '2026-05-17T12:00:00.000Z',
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    timeouts: { perTestMs: 30000 },
    passedCount: 4,
    failedCount: 2,
    timedOutCount: 0,
    totalCandidates: 6
  };
  const group = selectMostCommonFailureGroup({
    failed: [
      {
        path: 'css/css-backgrounds/background-attachment-fixed.html',
        timeout: false,
        diffRatio: 0.42,
        totalDurationMs: 2500,
        mismatch: {
          category: 'MissingContent',
          summary: 'Background content is missing.'
        }
      },
      {
        path: 'css/css-backgrounds/background-attachment-fixed-border-radius-offset.html',
        timeout: false,
        diffRatio: 0.17,
        totalDurationMs: 2000,
        mismatch: {
          category: 'MissingContent',
          summary: 'Background content is missing.'
        }
      }
    ]
  });

  const body = buildIssueBody(summary, group, 'https://github.com/MaiRat/Broiler.HTML/actions/runs/123/attempts/1', { exampleLimit: 2 });

  assert.match(body, /broiler-wpt-signature:mismatch:MissingContent/);
  assert.match(body, /Workflow run: \[https:\/\/github\.com\/MaiRat\/Broiler\.HTML\/actions\/runs\/123\/attempts\/1\]/);
  assert.match(body, /Representative failure/);
  assert.match(body, /Diff ratio: 42\.00%/);
  assert.match(body, /--include 'css\/css-backgrounds\/background-attachment-fixed\.html'/);
  assert.match(body, /Background content is missing\./);
});

test('main always creates a new issue without querying existing issues', async () => {
  const tempDirectory = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-issue-'));
  const summaryPath = path.join(tempDirectory, 'summary.json');
  await writeFile(summaryPath, JSON.stringify({
    generatedAt: '2026-05-17T12:00:00.000Z',
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    timeouts: { perTestMs: 30000 },
    passedCount: 4,
    failedCount: 1,
    timedOutCount: 0,
    totalCandidates: 5,
    failed: [
      {
        path: 'css/css-backgrounds/background-attachment-fixed.html',
        timeout: false,
        diffRatio: 0.42,
        totalDurationMs: 2500,
        mismatch: {
          category: 'MissingContent',
          summary: 'Background content is missing.'
        }
      }
    ]
  }), 'utf8');

  const originalFetch = globalThis.fetch;
  const requests = [];
  globalThis.fetch = async (url, init = {}) => {
    requests.push({ url, init });
    return {
      ok: true,
      json: async () => ({ number: 42, html_url: 'https://github.com/MaiRat/Broiler.HTML/issues/42' }),
      text: async () => ''
    };
  };

  try {
    const exitCode = await main(['--summary', summaryPath, '--repo', 'MaiRat/Broiler.HTML', '--example-limit', '1'], {
      ISSUE_TOKEN: 'token',
      GITHUB_SERVER_URL: 'https://github.com',
      GITHUB_RUN_ID: '123',
      GITHUB_RUN_ATTEMPT: '1'
    });

    assert.equal(exitCode, 0);
    assert.equal(requests.length, 1);
    assert.equal(requests[0].url, 'https://api.github.com/repos/MaiRat/Broiler.HTML/issues');
    assert.equal(requests[0].init.method, 'POST');
  } finally {
    globalThis.fetch = originalFetch;
    await rm(tempDirectory, { recursive: true, force: true });
  }
});

test('parseArguments reads summary and repository from the environment', () => {
  const options = parseArguments([], {
    SUMMARY_JSON: '/tmp/wpt/summary.json',
    GITHUB_REPOSITORY: 'MaiRat/Broiler.HTML',
    ISSUE_TOKEN: 'token'
  });

  assert.equal(options.summaryPath, path.resolve('/tmp/wpt/summary.json'));
  assert.equal(options.repository, 'MaiRat/Broiler.HTML');
  assert.equal(options.issueToken, 'token');
});

test('parseArguments rejects missing required inputs', () => {
  assert.throws(
    () => parseArguments([], { GITHUB_REPOSITORY: 'MaiRat/Broiler.HTML' }),
    /Missing summary path\. Pass --summary or set SUMMARY_JSON\./
  );
  assert.throws(
    () => parseArguments([], { SUMMARY_JSON: '/tmp/wpt/summary.json' }),
    /Missing repository\. Pass --repo or set GITHUB_REPOSITORY\./
  );
});

test('buildIssueTitle keeps a single shared path segment', () => {
  const group = selectMostCommonFailureGroup({
    failed: [
      { path: 'css', timeout: true, error: 'Render css with Broiler.HTML timed out after 30000ms.', totalDurationMs: 30000 },
      { path: 'css', timeout: true, error: 'Render css with Broiler.HTML timed out after 30000ms.', totalDurationMs: 29900 }
    ]
  });

  assert.equal(group.commonPathPrefix, 'css');
  assert.equal(buildIssueTitle(group), 'WPT non-JS CI: broiler-render timeout in css');
});

test('buildIssueBody shell-quotes rerun arguments for paths with apostrophes', () => {
  const group = selectMostCommonFailureGroup({
    failed: [
      {
        path: 'css/css-backgrounds/can\'t-render.html',
        timeout: false,
        diffRatio: 0.42,
        totalDurationMs: 2500,
        mismatch: {
          category: 'MissingContent',
          summary: 'Background content is missing.'
        }
      }
    ]
  });

  const body = buildIssueBody({
    generatedAt: '2026-05-17T12:00:00.000Z',
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    timeouts: { perTestMs: 30000 },
    passedCount: 0,
    failedCount: 1,
    timedOutCount: 0,
    totalCandidates: 1
  }, group, null, { exampleLimit: 1 });

  assert.match(body, /--include 'css\/css-backgrounds\/can'\\''t-render\.html'/);
});
