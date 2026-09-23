## Summary
- add explicit map-owned visual heightmap asset contracts and load them into map sessions
- remove engine-level flat visual heightmap fallback and enforce unique mounted asset resolution
- batch grounded prefab finalization in shared pipeline and add cross-adapter regression coverage

## Validation
- dotnet build .\src\Core\Ludots.Core.csproj
- dotnet test .\src\Tests\PresentationTests\PresentationTests.csproj --filter "FullyQualifiedName~PrefabFinalizationAndVisualHeightmapTests"
- dotnet test .\src\Tests\GasTests\GasTests.csproj --filter "FullyQualifiedName~MapManagerInheritanceTests|FullyQualifiedName~MapVisualHeightmapContractTests|FullyQualifiedName~UE5SkinnedPrefabFinalizationTests"
