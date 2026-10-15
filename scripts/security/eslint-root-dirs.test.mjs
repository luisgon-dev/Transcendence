import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const { getRootDirs } = require("@next/eslint-plugin-next/dist/utils/get-root-dirs.js");

test("Next ESLint root discovery retains directory-only brace globs through the scoped replacement", (t) => {
  const root = mkdtempSync(join(tmpdir(), "trn-eslint-roots-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, "web"));
  mkdirSync(join(root, "admin"));
  writeFileSync(join(root, "app.ts"), "");

  assert.deepEqual(getRootDirs({ cwd: root, settings: {} }), [root]);
  const directories = getRootDirs({ cwd: root, settings: { next: { rootDir: `${root}/{web,admin,app.ts}` } } });
  assert.deepEqual(directories.map((path) => resolve(path)).sort(), [join(root, "admin"), join(root, "web")]);
  const multiple = getRootDirs({ cwd: root, settings: { next: { rootDir: [`${root}/web`, `${root}/missing`] } } });
  assert.deepEqual(multiple.map((path) => resolve(path)), [join(root, "web")]);
});
