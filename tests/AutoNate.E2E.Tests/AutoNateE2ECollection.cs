using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Shared xUnit collection for every E2E test. The fixture spawns AutoNate.Web
/// + Playwright once and reuses it across all test classes — see
/// <see cref="AutoNateE2EFixture"/> for the parallelism rationale.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit collection-definition types are conventionally suffixed 'Collection'.")]
public sealed class AutoNateE2ECollection : ICollectionFixture<AutoNateE2EFixture>
{
    public const string Name = "AutoNate E2E";
}
