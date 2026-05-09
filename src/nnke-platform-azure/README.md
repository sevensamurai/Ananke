# nnke-platform-azure

Azure AI Agent Service adapter for [`nnke-platform`](../nnke-platform/README.md).

## Installation

```bash
dotnet tool install -g nnke-platform
dotnet tool install -g nnke-platform-azure
```

## Configuration

Set your Azure AI Foundry project endpoint before deploying:

```bash
export AZURE_AI_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>
```

Authentication uses `DefaultAzureCredential` — run `az login` or configure a managed identity.

## Usage

```bash
nnke-platform deploy --manifest my-workflow.yaml --platform azure-ai
nnke-platform status
nnke-platform teardown --deployment-id <id>
```

## How it works

When installed, this tool copies `nnke-platform-azure.dll` (and its Azure SDK dependencies)
into `~/.nnke-platform/adapters/`. On the next `nnke-platform` invocation the host probes
that directory, loads the DLL, and the module initializer registers `AzureAgentDeployer`
into `FederationDeployerRegistry` under the `"azure-ai"` platform key.
