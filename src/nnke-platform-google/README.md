# nnke-platform-google

Google Vertex AI / Gemini Agent Platform adapter for [`nnke-platform`](../nnke-platform/README.md).

## Installation

```bash
dotnet tool install -g nnke-platform
dotnet tool install -g nnke-platform-google
```

## Configuration

```bash
export GOOGLE_CLOUD_PROJECT=my-gcp-project
export GOOGLE_CLOUD_LOCATION=us-central1   # optional, defaults to us-central1
```

Authentication uses Application Default Credentials — run `gcloud auth application-default login`.

## Usage

```bash
nnke-platform deploy --manifest my-workflow.yaml --platform vertex-ai
nnke-platform status
nnke-platform teardown --deployment-id <id>
```
