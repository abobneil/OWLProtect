import { readFileSync, existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const requiredFiles = [
  "CONTRIBUTING.md",
  "README.md",
  "docker-compose.yml",
  ".env.example",
  ".env.local.example",
  ".env.selfhosted.example",
  ".github/CODEOWNERS",
  ".github/LABEL_TAXONOMY.md",
  ".github/PULL_REQUEST_TEMPLATE.md",
  ".github/ISSUE_TEMPLATE/bug.yml",
  ".github/ISSUE_TEMPLATE/docs.yml",
  ".github/ISSUE_TEMPLATE/feature.yml",
  "docs/architecture/overview.md",
  "docs/adr/template.md",
  "docs/adr/0001-postgres-and-redis-persistence.md",
  "docs/adr/0002-control-plane-issued-platform-sessions.md",
  "docs/adr/0003-control-plane-distributes-resolved-policy-bundles.md",
  "docs/foundation/contracts-versioning-and-config.md"
];

for (const relativePath of requiredFiles) {
  if (!existsSync(path.join(repoRoot, relativePath))) {
    fail(`Missing required Foundation file: ${relativePath}`);
  }
}

const envFiles = [
  ".env.example",
  ".env.local.example",
  ".env.selfhosted.example"
];

const envEntriesByFile = new Map(
  envFiles.map((relativePath) => [relativePath, parseEnvKeys(relativePath)])
);

const canonicalEnvKeys = envEntriesByFile.get(".env.example");
if (!canonicalEnvKeys) {
  fail("Unable to load .env.example.");
}

assertEvery(canonicalEnvKeys, (key) => key.startsWith("OWLP_"), "Foundation env keys must use the OWLP_ prefix.");

for (const [relativePath, keys] of envEntriesByFile.entries()) {
  assertEqualSets(
    canonicalEnvKeys,
    keys,
    `Environment example keys drifted in ${relativePath}.`
  );
}

const dockerComposeText = readText("docker-compose.yml");
const composeKeys = [...dockerComposeText.matchAll(/\$\{(OWLP_[A-Z0-9_]+)(?::-[^}]*)?\}/g)]
  .map((match) => match[1]);
assertEqualSets(
  canonicalEnvKeys,
  composeKeys,
  "docker-compose.yml environment inputs no longer match the documented Foundation env examples."
);

const foundationDoc = readText("docs/foundation/contracts-versioning-and-config.md");
assertIncludes(foundationDoc, "`packages/contracts`", "Foundation conventions doc must describe the shared contracts source of truth.");
assertIncludes(foundationDoc, "`/api/v1`", "Foundation conventions doc must define the versioned API prefix.");
assertIncludes(foundationDoc, "`OWLP_`", "Foundation conventions doc must define the root environment prefix.");

const issueTemplateFiles = [
  ".github/ISSUE_TEMPLATE/bug.yml",
  ".github/ISSUE_TEMPLATE/docs.yml",
  ".github/ISSUE_TEMPLATE/feature.yml"
];

for (const relativePath of issueTemplateFiles) {
  const text = readText(relativePath);
  assertIncludes(text, "CODEOWNERS", `${relativePath} must point contributors to CODEOWNERS ownership.`);
  assertIncludes(text, "taxonomy", `${relativePath} must reference the repository taxonomy.`);
}

const pullRequestTemplate = readText(".github/PULL_REQUEST_TEMPLATE.md");
assertIncludes(pullRequestTemplate, "CODEOWNERS", "PULL_REQUEST_TEMPLATE.md must reference CODEOWNERS review expectations.");
assertIncludes(pullRequestTemplate, "## Validation", "PULL_REQUEST_TEMPLATE.md must ask for validation evidence.");

console.log("Foundation validation passed.");

function parseEnvKeys(relativePath) {
  return readText(relativePath)
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"))
    .map((line) => {
      const separatorIndex = line.indexOf("=");
      if (separatorIndex <= 0) {
        fail(`Invalid environment entry in ${relativePath}: ${line}`);
      }

      return line.slice(0, separatorIndex);
    });
}

function readText(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

function assertEvery(values, predicate, message) {
  for (const value of values) {
    if (!predicate(value)) {
      fail(`${message} Offending value: ${value}`);
    }
  }
}

function assertEqualSets(expectedValues, actualValues, message) {
  const expected = new Set(expectedValues);
  const actual = new Set(actualValues);

  if (expected.size !== actual.size || [...expected].some((value) => !actual.has(value))) {
    const missing = [...expected].filter((value) => !actual.has(value));
    const extra = [...actual].filter((value) => !expected.has(value));
    fail(
      `${message} Missing: ${missing.join(", ") || "(none)"}; extra: ${extra.join(", ") || "(none)"}`
    );
  }
}

function assertIncludes(text, pattern, message) {
  if (!text.includes(pattern)) {
    fail(message);
  }
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
