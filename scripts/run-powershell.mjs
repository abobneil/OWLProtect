import { spawnSync } from "node:child_process";

const [, , scriptPath, ...scriptArgs] = process.argv;

if (!scriptPath) {
  console.error("Usage: node ./scripts/run-powershell.mjs <script-path> [args...]");
  process.exit(1);
}

const shellCommand = process.platform === "win32" ? "powershell" : "pwsh";
const result = spawnSync(shellCommand, ["-ExecutionPolicy", "Bypass", "-File", scriptPath, ...scriptArgs], {
  stdio: "inherit",
  shell: false
});

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}

process.exit(result.status ?? 0);
