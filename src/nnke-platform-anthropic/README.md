# nnke-platform-anthropic

Anthropic Claude Managed Agents adapter for nnke-platform. Preview - targets Anthropic Beta API (agents-2025-05-14).

## Installation

```bash
dotnet tool install -g nnke-platform
dotnet tool install -g nnke-platform-anthropic
```bash

## Configuration

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```bash

## Usage

```bash
nnke-platform deploy --manifest my-workflow.yaml --platform claude
nnke-platform status
nnke-platform teardown --deployment-id <id>
```
