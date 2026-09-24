import semanticRelease from "semantic-release";

const result = await semanticRelease(
  {
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

if (result?.nextRelease?.version) {
  process.stdout.write(result.nextRelease.version);
}
