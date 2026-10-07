// Calculates the next version with semantic-release's own commit analyzer. The strategy
// lives in release.config.cjs: `branches`, `tagFormat`, and the analyzer configuration are
// read from that file, so editing it changes what this script calculates. Only the commit
// analyzer is loaded, because it alone decides a version; the release-notes, exec, and
// github plugins act on a release instead of calculating one.
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import semanticRelease from "semantic-release";

const require = createRequire(import.meta.url);
const config = require(fileURLToPath(new URL("../release.config.cjs", import.meta.url)));

const analyzerName = "@semantic-release/commit-analyzer";
const analyzer = config.plugins.find((plugin) =>
  Array.isArray(plugin) ? plugin[0] === analyzerName : plugin === analyzerName,
);

const result = await semanticRelease(
  {
    branches: config.branches,
    tagFormat: config.tagFormat,
    plugins: [analyzer ?? analyzerName],
    ci: false,
    dryRun: true,
  },
  {
    cwd: process.cwd(),
    env: process.env,
    stderr: process.stderr,
    stdout: process.stderr,
  },
);

// No output means semantic-release declared no release for these commits.
process.stdout.write(result?.nextRelease?.version ?? "");
