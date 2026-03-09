import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const argv = process.argv.slice(2);
const mode = readFlag("mode") ?? "repo";
const remoteName = readFlag("remote-name") ?? "origin";
const repoRoot = runGit(["rev-parse", "--show-toplevel"]).trim();

process.chdir(repoRoot);

const blockedFilePatterns = [
  {
    label: "tracked environment file",
    test: (filePath) => {
      const baseName = path.posix.basename(filePath);
      return baseName.startsWith(".env") && !baseName.endsWith(".example");
    }
  },
  {
    label: "tracked secret directory",
    test: (filePath) => filePath.split("/").includes(".secrets")
  },
  {
    label: "tracked private-key file",
    test: (filePath) =>
      /\.(pem|key|p12|pfx|jks|keystore|kdbx|ovpn)$/i.test(filePath) ||
      /(^|\/)id_(rsa|dsa|ed25519)$/i.test(filePath)
  }
];

const secretPatterns = [
  {
    label: "private key block",
    regex: /-----BEGIN (?:RSA|DSA|EC|OPENSSH|PGP|PRIVATE) KEY-----/
  },
  {
    label: "AWS access key",
    regex: /AKIA[0-9A-Z]{16}/
  },
  {
    label: "Google API key",
    regex: /AIza[0-9A-Za-z_-]{35}/
  },
  {
    label: "GitHub token",
    regex: /(?:gh[pousr]_[A-Za-z0-9_]{36,255}|github_pat_[A-Za-z0-9_]{20,255})/
  },
  {
    label: "Slack token",
    regex: /xox[baprs]-[A-Za-z0-9-]{10,}/
  },
  {
    label: "Stripe key",
    regex: /sk_(?:live|test)_[0-9A-Za-z]{16,}/
  },
  {
    label: "SendGrid key",
    regex: /SG\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}/
  }
];

const secretKeyPattern = "[A-Za-z0-9_.-]*(?:password|passwd|pwd|token|secret|api[_-]?key|apikey|client[_-]?secret|private[_-]?key)\\b";
const directQuotedSecretPattern = new RegExp(`\\b(?<key>${secretKeyPattern})\\s*[:=]\\s*["'](?<value>[A-Za-z0-9._~+/=-]{12,})["']`, "i");
const fallbackQuotedSecretPattern = new RegExp(`\\b(?<key>${secretKeyPattern})\\s*[:=][^\\r\\n]{0,80}\\?\\?\\s*["'](?<value>[A-Za-z0-9._~+/=-]{12,})["']`, "i");
const envSecretPattern = /^(?<key>[A-Z0-9_.-]*(?:PASSWORD|PASSWD|PWD|SECRET|API[_-]?KEY|APIKEY|CLIENT[_-]?SECRET|PRIVATE[_-]?KEY|TOKEN)[A-Z0-9_.-]*)=(?<value>[A-Za-z0-9._~+\/=-]{12,})$/;
const findings = [];

switch (mode) {
  case "repo":
    scanTrackedFiles(listTrackedFiles());
    break;
  case "staged":
    scanStagedFiles(listStagedFiles());
    break;
  case "pre-push":
    scanPrePushCommits(remoteName, readStdin());
    break;
  default:
    console.error(`Unknown secret scan mode '${mode}'.`);
    process.exit(2);
}

if (findings.length > 0) {
  console.error("Secret scan failed:");
  for (const finding of findings) {
    console.error(`- ${finding.location}: ${finding.message}`);
  }
  console.error("Replace the secret, move it into a local secret store or ignored file, and try again.");
  process.exit(1);
}

console.log(`Secret scan passed (${mode}).`);

function readFlag(name) {
  const prefix = `--${name}=`;
  const value = argv.find((entry) => entry.startsWith(prefix));
  return value ? value.slice(prefix.length) : null;
}

function readStdin() {
  try {
    return readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

function runGit(args, options = {}) {
  return execFileSync("git", args, {
    cwd: process.cwd(),
    encoding: "utf8",
    stdio: ["pipe", "pipe", "pipe"],
    ...options
  });
}

function listTrackedFiles() {
  return runGit(["ls-files", "-z"]).split("\0").filter(Boolean);
}

function listStagedFiles() {
  return runGit(["diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z"]).split("\0").filter(Boolean);
}

function scanTrackedFiles(filePaths) {
  for (const filePath of filePaths) {
    scanPath(filePath, "working tree");
    const absolutePath = path.join(repoRoot, filePath);
    if (!existsSync(absolutePath)) {
      continue;
    }

    const content = readTextFile(absolutePath);
    if (content !== null) {
      scanText(filePath, content, "working tree");
    }
  }
}

function scanStagedFiles(filePaths) {
  for (const filePath of filePaths) {
    scanPath(filePath, "index");
    const content = runGit(["show", `:${filePath}`]);
    scanText(filePath, content, "index");
  }
}

function scanPrePushCommits(remote, stdinPayload) {
  const commits = collectPushCommits(remote, stdinPayload);
  for (const commit of commits) {
    const changedFiles = runGit(["diff-tree", "--no-commit-id", "--name-only", "-r", "--diff-filter=ACMR", "-z", commit])
      .split("\0")
      .filter(Boolean);

    for (const filePath of changedFiles) {
      scanPath(filePath, `commit ${commit.slice(0, 12)}`);
    }

    const patch = runGit(["show", "--format=", "--unified=0", "--no-ext-diff", "--no-textconv", commit, "--"]);
    scanPatch(commit, patch);
  }
}

function collectPushCommits(remote, stdinPayload) {
  const commits = new Set();
  const lines = stdinPayload.split(/\r?\n/).filter(Boolean);
  for (const line of lines) {
    const [, localSha, , remoteSha] = line.trim().split(/\s+/);
    if (!localSha || /^0+$/.test(localSha)) {
      continue;
    }

    const revListArgs = !remoteSha || /^0+$/.test(remoteSha)
      ? ["rev-list", localSha, "--not", `--remotes=${remote}`]
      : ["rev-list", `${remoteSha}..${localSha}`];

    for (const commit of runGit(revListArgs).split(/\r?\n/).filter(Boolean)) {
      commits.add(commit);
    }
  }

  return [...commits];
}

function scanPatch(commit, patch) {
  let currentFile = null;
  let currentLine = null;

  for (const rawLine of patch.split(/\r?\n/)) {
    if (rawLine.startsWith("+++ b/")) {
      currentFile = rawLine.slice("+++ b/".length);
      currentLine = null;
      continue;
    }

    const hunkMatch = rawLine.match(/^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/);
    if (hunkMatch) {
      currentLine = Number(hunkMatch[1]);
      continue;
    }

    if (rawLine.startsWith("+") && !rawLine.startsWith("+++")) {
      scanLine(currentFile ?? commit.slice(0, 12), currentLine ?? 0, rawLine.slice(1), `commit ${commit.slice(0, 12)}`);
      if (currentLine !== null) {
        currentLine += 1;
      }
      continue;
    }

    if (rawLine.startsWith("-") && !rawLine.startsWith("---")) {
      continue;
    }

    if (!rawLine.startsWith("\\") && currentLine !== null) {
      currentLine += 1;
    }
  }
}

function scanPath(filePath, sourceLabel) {
  const normalizedPath = normalizePath(filePath);
  for (const pattern of blockedFilePatterns) {
    if (pattern.test(normalizedPath)) {
      findings.push({
        location: normalizedPath,
        message: `${pattern.label} detected in ${sourceLabel}`
      });
    }
  }
}

function scanText(filePath, content, sourceLabel) {
  if (content.includes("\0")) {
    return;
  }

  const normalizedPath = normalizePath(filePath);
  const lines = content.split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    scanLine(normalizedPath, index + 1, lines[index], sourceLabel);
  }
}

function scanLine(filePath, lineNumber, line, sourceLabel) {
  const trimmed = line.trim();
  if (!trimmed || trimmed.startsWith("#")) {
    return;
  }

  for (const pattern of secretPatterns) {
    if (pattern.regex.test(line)) {
      findings.push({
        location: `${filePath}:${lineNumber}`,
        message: `${pattern.label} detected in ${sourceLabel}`
      });
    }
  }

  const assignmentMatch = line.match(directQuotedSecretPattern) ?? line.match(fallbackQuotedSecretPattern) ?? line.match(envSecretPattern);
  if (!assignmentMatch?.groups) {
    return;
  }

  if (isAllowedLiteral(filePath, assignmentMatch.groups.value)) {
    return;
  }

  findings.push({
    location: `${filePath}:${lineNumber}`,
    message: `possible hardcoded secret '${assignmentMatch.groups.key}' detected in ${sourceLabel}`
  });
}

function isAllowedLiteral(filePath, value) {
  const normalizedValue = value.toLowerCase();
  if (
    normalizedValue.includes("change-") ||
    normalizedValue.includes("replace-") ||
    normalizedValue.includes("placeholder") ||
    normalizedValue.includes("example") ||
    normalizedValue.includes("dummy") ||
    normalizedValue.includes("redacted")
  ) {
    return true;
  }

  if (filePath.endsWith(".example") && normalizedValue.length <= 64) {
    return true;
  }

  return false;
}

function readTextFile(filePath) {
  try {
    return readFileSync(filePath, "utf8");
  } catch {
    return null;
  }
}

function normalizePath(filePath) {
  return filePath.replace(/\\/g, "/");
}
