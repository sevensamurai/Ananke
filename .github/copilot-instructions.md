# Ananke - AI Agent Instructions

## Project Overview
Ananke is a C# .NET library project currently serving as a NuGet package name reservation. The project is configured for automated publishing to NuGet on merges to the main branch.

## Technology Stack
- **Language**: C# 
- **Framework**: .NET 10.0
- **Package Manager**: NuGet
- **CI/CD**: GitHub Actions
- **License**: Apache 2.0

## Project Structure
```
src/
  ├── Ananke.csproj      - Project configuration with NuGet metadata
  └── Placeholder.cs      - Placeholder implementation
.github/
  └── workflows/
      └── publish.yml     - Automated NuGet publishing workflow
```

## Coding Standards

### C# Style Guidelines
- Use modern C# language features and idiomatic patterns
- Follow Microsoft's C# coding conventions
- Use file-scoped namespaces where appropriate
- Prefer nullable reference types
- Use XML documentation comments for public APIs
- Keep code simple and maintainable

### Project Conventions
- Target framework: .NET 10.0
- Use minimal APIs and modern patterns
- Maintain backward compatibility when possible
- Keep dependencies minimal

## Version Management
- Version is specified in `src/Ananke.csproj` in the `<Version>` property
- Follow semantic versioning (MAJOR.MINOR.PATCH)
- Update version before merging to main for new releases
- Currently using placeholder version 0.0.1

## NuGet Publishing
- Automatic publishing occurs on merge to main branch
- GitHub Actions workflow handles build, pack, and publish
- Requires `NUGET_API_KEY` secret in GitHub repository settings
- Package includes symbols (snupkg) for debugging

## Development Workflow
1. Make changes in feature branches
2. Update version in Ananke.csproj if releasing
3. Test locally with `dotnet build` and `dotnet pack`
4. Create pull request to main
5. After merge, GitHub Actions automatically publishes to NuGet

## Key Files
- **Ananke.csproj**: Contains all NuGet metadata and package configuration
- **Placeholder.cs**: Current implementation (placeholder)
- **.github/workflows/publish.yml**: CI/CD pipeline configuration
- **README.md**: Project documentation and setup instructions

## Common Tasks

### Build the project
```bash
dotnet build src/Ananke.csproj
```

### Create NuGet package locally
```bash
dotnet pack src/Ananke.csproj --configuration Release --output ./packages
```

### Restore dependencies
```bash
dotnet restore src/Ananke.csproj
```

## AI Agent Guidelines
- Always maintain the project structure and conventions
- When adding new features, update documentation accordingly
- Ensure all changes maintain NuGet package compatibility
- Keep the codebase minimal and focused
- Test changes locally before committing
- Update version numbers appropriately for releases
- Follow semantic versioning principles
