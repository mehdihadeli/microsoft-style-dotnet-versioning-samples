module.exports = {
  branches: ["main"],
  tagFormat: "v${version}",
  plugins: [
    "@semantic-release/commit-analyzer",
    "@semantic-release/release-notes-generator",
    [
      "@semantic-release/exec",
      {
        publishCmd:
          "dotnet publish src/semantic-release-versioning-sample.csproj --configuration Release --output artifacts/${nextRelease.version} -p:Version=${nextRelease.version}",
      },
    ],
    "@semantic-release/github",
  ],
};
