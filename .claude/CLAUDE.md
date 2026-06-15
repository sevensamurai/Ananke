# Build & verify
- Build: `dotnet build --no-restore`
- Test: `dotnet test --no-build --logger "console;verbosity=normal"`
- Lint: `dotnet format --verify-no-changes`

# Architecture
- Pattern: Vertical Slice Architecture (Features/<FeatureName>/)

# C# conventions
- Use `TimeProvider` over `DateTime.Now` / `DateTime.UtcNow`
- Primary constructors for services (C# 12+)
- Records for DTOs and value objects
- `CancellationToken` on every async public method
- Collection expressions (`[.. list]`) over LINQ `Concat`

# Testing
- NUnit + Shouldly + NSubstitute
- Integration tests use `WebApplicationFactory<Program>`
- Test class naming: `<ClassName>Tests`; method naming: `<Method>_<Scenario>_<Expected>`

# Workflow
- Always `dotnet build` + `dotnet test` after a change set
- Open ADR in docs/adr/ before proposing architectural changes
- Do not modify .csproj files without confirmation

# Code navigation
- Use codegraph_explore and codegraph_search before reading files
- Only fall back to grep/glob if codegraph returns no results