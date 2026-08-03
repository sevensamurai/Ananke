# scripts/

Local developer scripts. Run these before opening a pull request.

| Script | Shell |
|---|---|
| [`check-docs.ps1`](#check-docsps1--documentation-drift-check) | Windows PowerShell 5.1 or `pwsh` |
| [`fix-encoding.ps1`](#fix-encodingps1--utf-8-bom-check) | **`pwsh` only** |

## `check-docs.ps1` — documentation drift check

Catches **stale type/API names in Markdown** before they reach a PR. The LLM-facing
docs (`docs/`, per-project `README.md` / `ARCHITECTURE.md`) are only useful if their
type inventories match the code. When a type is renamed — e.g. `GoogleAgentModel` →
`GeminiAgentModel`, or `IJobMiddleware` → `ITransitionMiddleware` — the prose silently
goes stale and an AI assistant reading it is primed with false facts.

### What it does

1. Builds a set of every PascalCase identifier (and source file name) that appears
   anywhere under `src/**/*.cs`.
2. Scans inline `` `code` `` spans in Markdown for backtick-quoted identifiers.
3. Flags any that exist **nowhere** in the source — the rename/typo/removal failure mode.

Any external type the codebase actually uses (e.g. `HttpClient`, `QdrantClient`) appears
in source and passes automatically, so false positives are low. It is **not** a semantic
check — it does not verify signatures or namespaces, only that a name exists. It
complements `dotnet build` / `dotnet test`; it does not replace them.

### Run it

```powershell
# Windows PowerShell 5.1
powershell -File scripts/check-docs.ps1

# pwsh (cross-platform)
pwsh scripts/check-docs.ps1
```

Exit code `0` = clean, `1` = drift found. Useful flags:

| Flag | Effect |
|---|---|
| `-ShowContext` | Print the offending line under each finding |
| `-IncludeCodeBlocks` | Also scan fenced ```` ```code``` ```` blocks (noisier; periodic deep audits) |
| `-IncludeExamples` | Also scan `src/demos/**` and `PLAN-*.md` (skipped by default) |
| `-Path <dirs>` | Override scan roots (default: `src`, `docs`) |

### When something is flagged

- **It's a renamed/typo'd Ananke symbol** → fix the doc (this is the point).
- **It's a genuine external library type, third-party product, MSBuild property, or a
  documented placeholder** → add it to [`check-docs-ignore.txt`](./check-docs-ignore.txt),
  with a comment. Prefer fixing the doc; only ignore when the name is truly not a code symbol.

### Optional: enforce on every push

Once the baseline is green, wire it into a git pre-push hook:

```bash
# .git/hooks/pre-push  (chmod +x)
#!/bin/sh
pwsh scripts/check-docs.ps1 || {
  echo "Docs drift check failed. Fix the stale identifiers above or update scripts/check-docs-ignore.txt."
  exit 1
}
```

The same script drops into CI unchanged (it already returns a non-zero exit code).

## `fix-encoding.ps1` — UTF-8 BOM check

Strips UTF-8 byte-order marks from tracked text files. A BOM breaks tooling that expects plain
UTF-8, so **CI runs this in `-Check` mode and fails the build on any finding**.

### Run it

```powershell
pwsh -File scripts/fix-encoding.ps1 -Check   # report only, exit 1 on findings
pwsh -File scripts/fix-encoding.ps1          # strip the BOMs in place
```

> **`pwsh` is required.** The script uses a PowerShell 7 ternary (`$Check ? … : …`) and fails with a
> parser error under Windows PowerShell 5.1 — a confusing failure, because it looks like the script
> is broken rather than the shell being wrong.

Covers `.cs`, `.csx`, `.md`, `.json`, `.yml`/`.yaml`, `.xml`, `.config`, `.txt`, `.csproj`, `.props`,
`.targets`, `.slnx`, `.nuspec`, `.resx`, `.sh` and `.editorconfig`, skipping `.git`, `obj`, `bin`,
`node_modules` and `.codegraph`. Exit code `0` = clean, `1` = BOMs found (in `-Check` mode).

Without `-Check` it **rewrites files in place** — run it on a clean working tree so the diff is
reviewable.
