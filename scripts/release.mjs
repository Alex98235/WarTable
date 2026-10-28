#!/usr/bin/env node
/**
 * Release helper.
 *
 * git-cliff reads tags; it cannot create them. This script closes that gap:
 * it asks git-cliff for the next version, shows the commits that would go into
 * the release, and — only after you confirm — creates an annotated tag and
 * pushes it. Pushing the tag is what triggers .github/workflows/release.yml.
 *
 *   npm run release              # interactive
 *   npm run release -- --dry-run # show what would happen
 *   npm run release -- --yes     # no prompt (for automation)
 */

import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { createInterface } from "node:readline/promises";
import path from "node:path";

const argv = process.argv.slice(2);
const DRY_RUN = argv.includes("--dry-run");
const ASSUME_YES = argv.includes("--yes") || argv.includes("-y");

const c = {
  dim: (s) => `\x1b[2m${s}\x1b[0m`,
  bold: (s) => `\x1b[1m${s}\x1b[0m`,
  red: (s) => `\x1b[31m${s}\x1b[0m`,
  yellow: (s) => `\x1b[33m${s}\x1b[0m`,
  green: (s) => `\x1b[32m${s}\x1b[0m`,
  cyan: (s) => `\x1b[36m${s}\x1b[0m`,
};

function fail(message) {
  console.error(`\n${c.red("✖")} ${message}\n`);
  process.exit(1);
}

function git(args, opts = {}) {
  return execFileSync("git", args, {
    encoding: "utf8",
    stdio: ["pipe", "pipe", "pipe"],
    ...opts,
  }).trim();
}

/** Locate git-cliff: env override, PATH, then the per-user install location. */
function findCliff() {
  const candidates = [
    process.env.GIT_CLIFF,
    "git-cliff",
    process.env.LOCALAPPDATA &&
      path.join(process.env.LOCALAPPDATA, "Programs", "git-cliff", "git-cliff.exe"),
    "/usr/local/bin/git-cliff",
    "/opt/homebrew/bin/git-cliff",
  ].filter(Boolean);

  for (const candidate of candidates) {
    if (candidate.includes(path.sep) && !existsSync(candidate)) continue;
    try {
      execFileSync(candidate, ["--version"], { stdio: "ignore" });
      return candidate;
    } catch {
      /* try next */
    }
  }
  return null;
}

function cliff(cmd, args) {
  return execFileSync(cmd, args, {
    encoding: "utf8",
    stdio: ["pipe", "pipe", "pipe"],
  }).trim();
}

/**
 * Same as cliff(), but a non-zero exit with no stdout is treated as an empty
 * answer. git-cliff does this for --bumped-version when the repository has no
 * tags: it computes 0.1.0 internally, then fails its own tag-pattern check.
 */
function cliffOptional(cmd, args) {
  try {
    return cliff(cmd, args);
  } catch (err) {
    return (err.stdout ?? "").toString().trim();
  }
}

// ---------------------------------------------------------------- checks

const cliffCmd = findCliff();
if (!cliffCmd) {
  fail(
    "git-cliff not found.\n" +
      "  Install it with: scoop install git-cliff\n" +
      "             or:  cargo install git-cliff\n" +
      "  Or set GIT_CLIFF to the binary path."
  );
}

const branch = git(["rev-parse", "--abbrev-ref", "HEAD"]);
if (branch !== "main") {
  fail(`Releases must be tagged from main (currently on '${branch}').`);
}

const dirty = git(["status", "--porcelain"]);
if (dirty) {
  fail(`Working tree is not clean. Commit or stash first:\n\n${dirty}`);
}

// Make sure the bumped version is computed against the real tag list.
try {
  git(["fetch", "--tags", "--quiet", "origin"]);
} catch {
  console.warn(c.yellow("!") + " Could not fetch tags from origin; using local tags.");
}

// git-cliff needs at least one tag as a baseline to bump from; on a brand new
// repository it prints nothing. That is the first-release case, not an error.
let version = cliffOptional(cliffCmd, ["--bumped-version"]);
const firstRelease = !version;
if (firstRelease) {
  version = "v0.1.0";
}
if (!/^v\d+\.\d+\.\d+/.test(version)) {
  fail(`git-cliff returned an unexpected version: '${version}'`);
}

let tagExists = true;
try {
  git(["rev-parse", "--verify", "--quiet", `refs/tags/${version}`]);
} catch {
  tagExists = false;
}
if (tagExists) {
  fail(`Tag ${version} already exists. Bump the version or check 'git tag -l'.`);
}

let notes = cliffOptional(cliffCmd, ["--unreleased", "--strip", "header"]);
if (firstRelease && !notes) {
  // No tags means no "unreleased" range; fall back to the whole history.
  notes = cliffOptional(cliffCmd, ["--strip", "header"]).replace(/^##\s*\[Unreleased\]\s*/m, "");
}
const head = git(["rev-parse", "--short", "HEAD"]);

// ---------------------------------------------------------------- preview

console.log();
console.log(c.bold(`  Release ${version}`) + c.dim(`  (from ${head} on ${branch})`));
if (firstRelease) {
  console.log(c.dim("  First release — no tags found, so the version starts at v0.1.0."));
}
console.log();
console.log(notes || c.dim("  (no commits since the last tag)"));
console.log();
console.log(c.dim("  This creates an annotated tag and pushes it. The tag push triggers"));
console.log(c.dim("  .github/workflows/release.yml, which builds both binaries and"));
console.log(c.dim("  publishes the GitHub release using the notes above."));
console.log();

if (DRY_RUN) {
  console.log(c.cyan("DRY RUN") + " — nothing was tagged or pushed.\n");
  process.exit(0);
}

// ---------------------------------------------------------------- confirm

let proceed = ASSUME_YES;
if (!ASSUME_YES) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  const answer = (await rl.question(`  Tag and push ${c.bold(version)}? [y/N] `)).trim().toLowerCase();
  rl.close();
  proceed = answer === "y" || answer === "yes";
}

if (!proceed) {
  console.log("\n  Aborted. Nothing was changed.\n");
  process.exit(0);
}

// ---------------------------------------------------------------- tag + push

console.log();
try {
  git(["tag", "-a", version, "-m", version]);
  console.log(`  ${c.green("✔")} created tag ${version}`);

  git(["push", "origin", version]);
  console.log(`  ${c.green("✔")} pushed ${version} to origin`);
} catch (err) {
  const detail = err.stderr?.toString().trim() || err.message;
  fail(`Release failed: ${detail}`);
}

console.log();
console.log(`  ${c.green("Done.")} Watch the workflow:`);
console.log(c.dim(`  https://github.com/${git(["remote", "get-url", "origin"]).replace(/.*github\.com[:/]|\.git$/g, "")}/actions`));
console.log();
