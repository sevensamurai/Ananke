# nnke

A .NET global tool for scaffolding Ananke workflow projects and manifest files.

## Install

```bash
dotnet tool install -g nnke
```

## Usage

```bash
# Scaffold a complete workflow project
nnke new workflow MyPipeline

# Scaffold with a specific LLM provider and topology pattern
nnke new workflow MyPipeline --provider anthropic --pattern fan-out

# Generate a standalone .ananke.yml manifest
nnke new manifest my-etl --pattern etl

# Validate a manifest file
nnke validate my-etl.ananke.yml

# Export a Mermaid diagram from a manifest
nnke diagram my-etl.ananke.yml
```

## Commands

| Command | Description |
|---|---|
| `nnke new workflow <name>` | Scaffold a runnable workflow project (`.csproj`, `Program.cs`, `.ananke.yml`, state record, secrets template) |
| `nnke new manifest <name>` | Generate a standalone `.ananke.yml` starter file |
| `nnke validate <file>` | Parse and validate manifest topology |
| `nnke diagram <file>` | Export Mermaid flowchart from manifest connections |
