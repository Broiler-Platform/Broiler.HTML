import assert from 'node:assert/strict';
import test from 'node:test';

import {
  buildIssueBody,
  buildIssueTitle,
  parseArguments,
  selectMostCommonFailureGroup,
  shouldCreateIssue
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

test('shouldCreateIssue blocks matching open or recent issues only', () => {
  const now = new Date('2026-05-17T13:00:00.000Z');
  const matchingClosedIssue = {
    state: 'closed',
    title: 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds',
    body: '<!-- broiler-wpt-signature:timeout:broiler-render -->',
    created_at: '2026-05-11T12:00:00.000Z'
  };
  const oldMatchingClosedIssue = {
    state: 'closed',
    title: 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds',
    body: '<!-- broiler-wpt-signature:timeout:broiler-render -->',
    created_at: '2026-05-01T12:00:00.000Z'
  };
  const matchingOpenTitleOnlyIssue = {
    state: 'open',
    title: 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds',
    body: 'Manually created tracker issue.'
  };

  assert.equal(shouldCreateIssue([matchingClosedIssue], 'timeout:broiler-render', 7, now, 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds'), false);
  assert.equal(shouldCreateIssue([oldMatchingClosedIssue], 'timeout:broiler-render', 7, now, 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds'), true);
  assert.equal(shouldCreateIssue([matchingOpenTitleOnlyIssue], 'timeout:broiler-render', 7, now, 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds'), false);
  assert.equal(shouldCreateIssue([{ state: 'open', body: '<!-- broiler-wpt-signature:mismatch:MissingContent -->' }], 'timeout:broiler-render', 7, now, 'WPT non-JS CI: broiler-render timeout in css/css-backgrounds'), true);
});

test('parseArguments reads summary and repository from the environment', () => {
  const options = parseArguments([], {
    SUMMARY_JSON: '/tmp/wpt/summary.json',
    GITHUB_REPOSITORY: 'MaiRat/Broiler.HTML',
    ISSUE_TOKEN: 'token'
  });

  assert.equal(options.summaryPath, '/tmp/wpt/summary.json');
  assert.equal(options.repository, 'MaiRat/Broiler.HTML');
  assert.equal(options.issueToken, 'token');
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
