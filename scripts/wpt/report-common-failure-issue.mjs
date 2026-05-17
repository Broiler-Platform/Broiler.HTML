#!/usr/bin/env node
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRecentIssueWindowDays = 7;
const defaultExampleLimit = 5;
const issueSignaturePrefix = 'broiler-wpt-signature:';

async function main(argv = process.argv.slice(2), env = process.env) {
  const options = parseArguments(argv, env);
  const summary = await readSummary(options.summaryPath);
  if (!summary) {
    console.log(`No WPT summary JSON found at ${options.summaryPath}; skipping issue creation.`);
    return 0;
  }

  if (!summary.failedCount || !Array.isArray(summary.failed) || summary.failed.length === 0) {
    console.log('The WPT summary contains no failures; skipping issue creation.');
    return 0;
  }

  if (!options.issueToken) {
    console.log('ISSUE_TOKEN is not set; skipping WPT failure issue creation.');
    return 0;
  }

  const repository = parseRepository(options.repository);
  const failureGroup = selectMostCommonFailureGroup(summary);
  if (!failureGroup) {
    console.log('No WPT failure group could be derived from the summary; skipping issue creation.');
    return 0;
  }

  const recentIssues = await listRepositoryIssues(repository, options.issueToken);
  const issueTitle = buildIssueTitle(failureGroup);
  if (!shouldCreateIssue(recentIssues, failureGroup.signature, options.recentIssueWindowDays, new Date(), issueTitle)) {
    console.log(`A matching open or recent issue already exists for ${failureGroup.signature}; skipping creation.`);
    return 0;
  }

  const workflowRunUrl = buildWorkflowRunUrl(options);
  const issue = await createIssue(repository, options.issueToken, {
    title: issueTitle,
    body: buildIssueBody(summary, failureGroup, workflowRunUrl, options)
  });

  console.log(`Created WPT failure issue #${issue.number}: ${issue.html_url}`);
  return 0;
}

function parseArguments(args, env = process.env) {
  const options = {
    summaryPath: env.SUMMARY_JSON ? path.resolve(env.SUMMARY_JSON) : null,
    repository: env.GITHUB_REPOSITORY ?? null,
    issueToken: env.ISSUE_TOKEN ?? '',
    recentIssueWindowDays: defaultRecentIssueWindowDays,
    exampleLimit: defaultExampleLimit,
    serverUrl: env.GITHUB_SERVER_URL ?? 'https://github.com',
    runId: env.GITHUB_RUN_ID ?? null,
    runAttempt: env.GITHUB_RUN_ATTEMPT ?? null
  };

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    switch (argument) {
      case '--summary':
        options.summaryPath = path.resolve(readValue(args, ++index, argument));
        break;
      case '--repo':
        options.repository = readValue(args, ++index, argument);
        break;
      case '--recent-days':
        options.recentIssueWindowDays = readInteger(args, ++index, argument, 1);
        break;
      case '--example-limit':
        options.exampleLimit = readInteger(args, ++index, argument, 1);
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  if (!options.summaryPath) {
    throw new Error('Missing summary path. Pass --summary or set SUMMARY_JSON.');
  }

  if (!options.repository) {
    throw new Error('Missing repository. Pass --repo or set GITHUB_REPOSITORY.');
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

function readInteger(args, index, argumentName, min) {
  const rawValue = readValue(args, index, argumentName);
  const value = Number.parseInt(rawValue, 10);
  if (!Number.isInteger(value) || value < min) {
    throw new Error(`Invalid integer value for ${argumentName}: ${rawValue}`);
  }

  return value;
}

async function readSummary(summaryPath) {
  try {
    return JSON.parse(await fs.readFile(summaryPath, 'utf8'));
  } catch (error) {
    if (error && typeof error === 'object' && 'code' in error && error.code === 'ENOENT') {
      return null;
    }

    throw error;
  }
}

function parseRepository(repository) {
  const [owner, repo] = repository.split('/');
  if (!owner || !repo) {
    throw new Error(`Invalid repository identifier: ${repository}`);
  }

  return { owner, repo };
}

function normalizeFailure(entry) {
  const mismatch = normalizeMismatch(entry?.mismatch);
  return {
    path: typeof entry?.path === 'string' ? entry.path : null,
    timeout: Boolean(entry?.timeout),
    timeoutPhase: typeof entry?.timeoutPhase === 'string' && entry.timeoutPhase ? entry.timeoutPhase : inferTimeoutPhase(entry?.error),
    totalDurationMs: normalizeFiniteNumber(entry?.totalDurationMs),
    diffRatio: normalizeFiniteNumber(entry?.diffRatio),
    error: typeof entry?.error === 'string' && entry.error ? entry.error : null,
    mismatch
  };
}

function normalizeMismatch(mismatch) {
  if (!mismatch || typeof mismatch !== 'object') {
    return null;
  }

  return {
    category: typeof mismatch.category === 'string' && mismatch.category ? mismatch.category : null,
    summary: typeof mismatch.summary === 'string' && mismatch.summary ? mismatch.summary : null,
    averageChannelDelta: normalizeFiniteNumber(mismatch.averageChannelDelta),
    maxChannelDelta: normalizeFiniteNumber(mismatch.maxChannelDelta),
    affectedRows: normalizeFiniteNumber(mismatch.affectedRows),
    affectedColumns: normalizeFiniteNumber(mismatch.affectedColumns)
  };
}

function inferTimeoutPhase(error) {
  if (typeof error !== 'string') {
    return null;
  }

  if (error.includes('with Broiler.HTML timed out')) {
    return 'broiler-render';
  }
  if (error.includes('Chromium reference')) {
    return 'chromium-reference';
  }
  if (error.startsWith('Compare ')) {
    return 'image-compare';
  }

  return null;
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

function selectMostCommonFailureGroup(summary) {
  const groups = new Map();
  for (const entry of summary.failed ?? []) {
    const failure = normalizeFailure(entry);
    if (!failure.path) {
      continue;
    }

    const group = groups.get(failureSignature(failure)) ?? createFailureGroup(summary, failure);
    group.failures.push(failure);
    groups.set(group.signature, group);
  }

  const sortedGroups = [...groups.values()].sort(compareFailureGroups);
  if (sortedGroups.length === 0) {
    return null;
  }

  const topGroup = sortedGroups[0];
  topGroup.failures.sort(compareRepresentativeFailures);
  topGroup.representative = topGroup.failures[0];
  topGroup.examplePaths = topGroup.failures.slice(0, defaultExampleLimit).map((failure) => failure.path);
  topGroup.commonPathPrefix = findCommonPathPrefix(topGroup.examplePaths);
  return topGroup;
}

function createFailureGroup(summary, failure) {
  const signature = failureSignature(failure);
  return {
    signature,
    kind: failure.timeout ? 'timeout' : 'visual',
    label: describeFailureGroup(failure),
    failures: [],
    summary,
    representative: null,
    examplePaths: [],
    commonPathPrefix: ''
  };
}

function failureSignature(failure) {
  if (failure.timeout) {
    return `timeout:${failure.timeoutPhase ?? 'unknown'}`;
  }

  return `mismatch:${failure.mismatch?.category ?? 'unknown'}`;
}

function describeFailureGroup(failure) {
  if (failure.timeout) {
    return `${failure.timeoutPhase ?? 'unknown'} timeout`;
  }

  return `${failure.mismatch?.category ?? 'unknown'} mismatch`;
}

function compareFailureGroups(left, right) {
  const countDifference = right.failures.length - left.failures.length;
  if (countDifference !== 0) {
    return countDifference;
  }

  return compareRepresentativeFailures(
    normalizeFailure(left.representative ?? left.failures[0]),
    normalizeFailure(right.representative ?? right.failures[0])
  );
}

function compareRepresentativeFailures(left, right) {
  const rightScore = failureSeverityScore(right);
  const leftScore = failureSeverityScore(left);
  if (rightScore !== leftScore) {
    return rightScore - leftScore;
  }

  return (left.path ?? '').localeCompare(right.path ?? '');
}

function failureSeverityScore(failure) {
  if (failure.timeout) {
    return failure.totalDurationMs ?? 0;
  }

  return failure.diffRatio ?? 0;
}

function findCommonPathPrefix(paths) {
  if (!Array.isArray(paths) || paths.length === 0) {
    return '';
  }

  const pathSegments = paths.map((entry) => entry.split('/'));
  const prefix = [];
  for (let index = 0; index < pathSegments[0].length - 1; index += 1) {
    const segment = pathSegments[0][index];
    if (!segment || !pathSegments.every((parts) => parts[index] === segment)) {
      break;
    }
    prefix.push(segment);
  }

  return prefix.join('/');
}

function buildIssueTitle(group) {
  const scope = group.commonPathPrefix ? ` in ${group.commonPathPrefix}` : '';
  return `WPT non-JS CI: ${group.label}${scope}`;
}

function buildIssueBody(summary, group, workflowRunUrl, options) {
  const representative = group.representative;
  const rerunArgs = group.failures
    .slice(0, options.exampleLimit)
    .map((failure) => `--include ${shellQuote(failure.path)}`)
    .join(' ');

  const lines = [
    `<!-- ${issueSignaturePrefix}${group.signature} -->`,
    '# Common WPT CI failure detected',
    '',
    `The non-JS WPT workflow found **${group.failures.length}** occurrence(s) of the most common failure group: **${group.label}**.`,
    '',
    '## Workflow context',
    '',
    `- Generated at: ${summary.generatedAt ?? 'n/a'}`,
    `- Workflow run: ${workflowRunUrl ? `[${workflowRunUrl}](${workflowRunUrl})` : 'n/a'}`,
    `- WPT root: \`${summary.wptRoot ?? 'n/a'}\``,
    `- Output root: \`${summary.outputRoot ?? 'n/a'}\``,
    `- Viewport: ${summary.viewport?.width ?? 'n/a'}x${summary.viewport?.height ?? 'n/a'}`,
    `- Per-test timeout: ${summary.timeouts?.perTestMs ?? 'n/a'} ms`,
    `- Totals: ${summary.passedCount ?? 0} passed / ${summary.failedCount ?? 0} failed / ${summary.timedOutCount ?? 0} timed out / ${summary.totalCandidates ?? 0} selected`,
    '',
    '## Representative failure',
    '',
    `- Test: \`${representative?.path ?? 'n/a'}\``,
    `- Signature: \`${group.signature}\``,
    representative?.timeout
      ? `- Timeout phase: \`${representative.timeoutPhase ?? 'unknown'}\``
      : `- Mismatch category: \`${representative?.mismatch?.category ?? 'unknown'}\``,
    representative?.timeout
      ? `- Error: ${representative.error ?? 'n/a'}`
      : `- Diff ratio: ${formatDiffRatio(representative?.diffRatio)}`,
    representative?.mismatch?.summary ? `- Diagnostic summary: ${representative.mismatch.summary}` : null,
    representative?.totalDurationMs !== null && representative?.totalDurationMs !== undefined
      ? `- Duration: ${formatDuration(representative.totalDurationMs)}`
      : null,
    '',
    `## Affected examples (top ${Math.min(group.failures.length, options.exampleLimit)})`,
    ''
  ].filter(Boolean);

  for (const failure of group.failures.slice(0, options.exampleLimit)) {
    lines.push(`- \`${failure.path}\`${formatFailureDetailSuffix(failure)}`);
  }

  lines.push(
    '',
    '## Focused rerun arguments',
    '',
    '```bash',
    rerunArgs || '# No rerun arguments available.',
    '```'
  );

  return `${lines.join('\n')}\n`;
}

function formatFailureDetailSuffix(failure) {
  if (failure.timeout) {
    return ` — timeout phase=${failure.timeoutPhase ?? 'unknown'}${failure.error ? `; ${failure.error}` : ''}`;
  }

  const diffRatio = formatDiffRatio(failure.diffRatio);
  const mismatchSummary = failure.mismatch?.summary ? `; ${failure.mismatch.summary}` : '';
  return ` — diff=${diffRatio}; ${failure.mismatch?.category ?? 'unknown'}${mismatchSummary}`;
}

function formatDiffRatio(value) {
  return value === null || value === undefined ? 'n/a' : `${(value * 100).toFixed(2)}%`;
}

function formatDuration(durationMs) {
  if (!Number.isFinite(durationMs)) {
    return 'n/a';
  }

  if (durationMs >= 1000) {
    return `${(durationMs / 1000).toFixed(1)}s`;
  }

  return `${durationMs.toFixed(0)}ms`;
}

function shellQuote(value) {
  return `'${String(value).replaceAll('\'', '\'\\\'\'')}'`;
}

function buildWorkflowRunUrl(options) {
  if (!options.serverUrl || !options.repository || !options.runId) {
    return null;
  }

  const attemptSuffix = options.runAttempt ? `/attempts/${options.runAttempt}` : '';
  return `${options.serverUrl}/${options.repository}/actions/runs/${options.runId}${attemptSuffix}`;
}

function shouldCreateIssue(issues, signature, recentIssueWindowDays, now = new Date(), title = null) {
  const signatureMarker = `${issueSignaturePrefix}${signature}`;
  const recentThreshold = new Date(now.getTime() - recentIssueWindowDays * 24 * 60 * 60 * 1000);

  return !issues.some((issue) => {
    const titleMatches = typeof issue?.title === 'string' && title !== null && issue.title === title;
    const signatureMatches = typeof issue?.body === 'string' && issue.body.includes(signatureMarker);
    if (!titleMatches && !signatureMatches) {
      return false;
    }

    if (issue.state === 'open') {
      return true;
    }

    const createdAt = typeof issue.created_at === 'string' ? new Date(issue.created_at) : null;
    return createdAt instanceof Date && !Number.isNaN(createdAt.valueOf()) && createdAt >= recentThreshold;
  });
}

async function listRepositoryIssues(repository, issueToken) {
  const searchParameters = new URLSearchParams({
    state: 'all',
    sort: 'created',
    direction: 'desc',
    per_page: '100'
  });

  return githubApiRequest(
    repository,
    issueToken,
    `/repos/${repository.owner}/${repository.repo}/issues?${searchParameters.toString()}`
  ).then((issues) => issues.filter((issue) => !issue.pull_request));
}

async function createIssue(repository, issueToken, issue) {
  return githubApiRequest(
    repository,
    issueToken,
    `/repos/${repository.owner}/${repository.repo}/issues`,
    {
      method: 'POST',
      body: JSON.stringify(issue)
    }
  );
}

async function githubApiRequest(repository, issueToken, apiPath, init = {}) {
  const response = await fetch(`https://api.github.com${apiPath}`, {
    ...init,
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${issueToken}`,
      'Content-Type': 'application/json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': `${repository.owner}-${repository.repo}-wpt-ci`,
      ...init.headers
    }
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`GitHub API request failed (${response.status} ${response.statusText}): ${body}`);
  }

  return response.json();
}

export {
  buildIssueBody,
  buildIssueTitle,
  failureSignature,
  formatDiffRatio,
  inferTimeoutPhase,
  main,
  normalizeFailure,
  parseArguments,
  selectMostCommonFailureGroup,
  shouldCreateIssue
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
