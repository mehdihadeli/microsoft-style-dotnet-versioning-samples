using Xunit;

namespace semantic_release_versioning_sample.Tests;

[CollectionDefinition("semantic-release", DisableParallelization = true)]
public sealed class SemanticReleaseCollection : ICollectionFixture<SemanticReleaseTestFixture>;
