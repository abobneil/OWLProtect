import { execFileSync } from "node:child_process";

function tryGit(args, cwd = process.cwd()) {
  try {
    return execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  } catch {
    return null;
  }
}

const repoRoot = tryGit(["rev-parse", "--show-toplevel"]);
if (!repoRoot) {
  process.exit(0);
}

const currentHooksPath = tryGit(["config", "--get", "core.hooksPath"], repoRoot);
if (currentHooksPath && currentHooksPath !== ".githooks") {
  console.warn(`Skipping git hook setup because core.hooksPath is already set to '${currentHooksPath}'.`);
  process.exit(0);
}

execFileSync("git", ["config", "core.hooksPath", ".githooks"], {
  cwd: repoRoot,
  stdio: "inherit"
});

console.log("Configured git hooks path: .githooks");
