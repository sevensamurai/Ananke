# Ananke.Embeddings.Onnx

[![NuGet](https://img.shields.io/nuget/v/Ananke.Embeddings.Onnx.svg)](https://www.nuget.org/packages/Ananke.Embeddings.Onnx)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Local embeddings for Ananke: `OnnxEmbeddingModel` is an `IEmbeddingModel` that runs a BERT-style encoder on ONNX
Runtime, so the text a knowledge store embeds never leaves the host. Experimental (`ANNKEEXP004`).

## Install

```bash
dotnet add package Ananke.Embeddings.Onnx
```

## Quick start

Point it at a directory holding the ONNX file and the model's vocabulary (`vocab.txt`, or `tokenizer.json`). Nothing
is downloaded. For BAAI/bge-small-en-v1.5, the quantized export in `Qdrant/bge-small-en-v1.5-onnx-Q` names its file
`model_optimized.onnx` and is read by its `[CLS]` vector:

```csharp
#pragma warning disable ANNKEEXP004
using Ananke.Embeddings.Onnx;
using Ananke.Orchestration.Knowledge;

using var embedder = new OnnxEmbeddingModel(new OnnxEmbeddingOptions
{
    ModelDirectory = "/models/bge-small-en-v1.5",
    ModelFile = "model_optimized.onnx",
    Pooling = OnnxPooling.Cls,
});

var store = new InMemoryKnowledgeStore(embedder);
```

A sentence-transformers model (all-MiniLM-L6-v2 and similar) is read by the mean of its token vectors:
`Pooling = OnnxPooling.Mean`.

`embedder.Identity` names the model (`onnx/bge-small-en-v1.5 (384)`), so a store can refuse vectors from another.

## Options

| Option | Default | What it does |
|---|---|---|
| `ModelDirectory` | required | Where the model and its vocabulary are |
| `ModelFile` | `model.onnx` | The ONNX file in that directory |
| `ModelName`, `Revision` | the directory's name, none | The model's identity |
| `Pooling` | `Cls` | `Cls` takes the first token's vector; `Mean` averages the text's tokens |
| `Normalize` | `true` | Scales each vector to length 1 |
| `LowerCase` | `true` | Lower-cases text before splitting it, as uncased models need |
| `MaxTokens` | 512 | The most tokens a text is read to, `[CLS]` and `[SEP]` included |
| `BatchSize` | 16 | How many texts run through the model at once |
