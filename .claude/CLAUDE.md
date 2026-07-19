# Build & verify
- Build: `dotnet build --no-restore`
- Test: `dotnet test --no-build --logger "console;verbosity=normal"`

# Only before create a pull request
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
- When docs (`docs/`, any `README.md` / `ARCHITECTURE.md`) are touched, run `powershell -File scripts/check-docs.ps1` before opening a PR — it flags stale type/API names. See `scripts/README.md`.
- Open ADR in internals/design/ before proposing architectural changes
- Do not modify .csproj files without confirmation
- Never commit files with a UTF-8 BOM; CI runs `scripts/fix-encoding.ps1 -Check`
- Provider model lineups (OpenAI/Anthropic/Google) change every few months — check each release
  whether `Models.cs` and both `ModelCatalog`s need new entries. See "Keeping the model catalog
  current" in `src/Ananke.Design/README.md`.
- Referencing a deprecated or retired model id (literal or constant) outside an annotated
  `#pragma warning disable ANNKE00x` block is a build error, not a warning — see
  `docs/reference/model-deprecations.md`.

# Code navigation
- Read `MAP.md` first to find which architecture doc / guide / source dir covers a concept
- Use codegraph_explore and codegraph_search before reading files
- Only fall back to grep/glob if codegraph returns no results
