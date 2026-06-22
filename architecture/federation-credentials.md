# Federation Credentials Matrix

This document describes the credential types, required scopes, rotation procedures, and
implementation status for each platform adapter in `Ananke.Federation`.

> **See also:** [`organics-federation.md`](organics-federation.md) for the overall
> federation architecture. The `IFederationCredentialProvider` interface lives in
> `src/Ananke.Federation/Credentials/IFederationCredentialProvider.cs`.

---

## Matrix

| Platform | Credential type | Required scopes / permissions | Rotation procedure | `IFederationCredentialProvider` implementation | Status |
|---|---|---|---|---|---|
| **Azure** | Azure Managed Identity _or_ Service Principal (client secret / certificate) | `Cognitive Services User`, `AzureML Data Scientist` (or equivalent for the target resource) | Rotate via Azure Key Vault secret rotation policy; MSI rotates automatically | `Ananke.Federation.Azure` — `AzureAgentCredentialProvider` | Implemented |
| **Google** | Service Account JSON key _or_ Workload Identity Federation | `roles/aiplatform.user` on the Vertex AI project | Rotate service account keys via `gcloud iam service-accounts keys create`; prefer Workload Identity (keyless) | `Ananke.Federation.Google` — `VertexAICredentialProvider` | Implemented |
| **Anthropic** | API key (bearer token) | N/A — key grants full account access; scope via sub-keys if available | Rotate via Anthropic Console; store in secrets manager, never in source | `Ananke.Federation.Anthropic` — `ClaudeCredentialProvider` | Implemented |
| **Local** | None — in-process, no remote auth | N/A | N/A | N/A (no credential provider needed for local deployments) | Implemented |

---

## `IFederationCredentialProvider` interface

```csharp
namespace Ananke.Federation.Credentials;

public interface IFederationCredentialProvider
{
    string Platform { get; }
    Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default);
    Task<bool> ValidateAsync(CancellationToken ct = default);
}
```

- `GetCredentialAsync` — resolves the raw credential object at runtime. Secrets are never
  stored in manifests; this method fetches them on demand from the host secrets store.
- `ValidateAsync` — calls the platform and confirms the credential is accepted. Useful for
  `nnke-platform whoami` and startup health checks. This is a plain interface member with
  no default implementation — every provider supplies its own:
  - **`ClaudeCredentialProvider`** — without a `clientFactory`, returns `true` when
    `ANTHROPIC_API_KEY` (or the constructor-supplied key) is present and non-empty. With a
    `clientFactory` supplied, performs a live API round-trip (`PingAsync`).
  - **`AzureAgentCredentialProvider`** / **`VertexAICredentialProvider`** — call
    `GetCredentialAsync(Platform, ct)` and return whether the result is non-null.

---

## Secrets storage guidance

| Environment | Recommended store |
|---|---|
| Local development | User secrets (`dotnet user-secrets`) or `.env` file (gitignored) |
| CI/CD | GitHub Actions encrypted secrets / Azure DevOps variable groups |
| Production (Azure) | Azure Key Vault + Managed Identity reference |
| Production (GCP) | Secret Manager + Workload Identity |
| Production (self-hosted) | HashiCorp Vault or equivalent |

Never commit API keys or service account JSON files to source control.
The `~/.ananke/credentials.json` file written by `nnke-platform login` is stored
with `chmod 600` (user-read only) and should be excluded from version control via
`.gitignore`.
