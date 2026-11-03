# Ananke.Embeddings.Onnx — Architecture

> Local embeddings: a BERT-style encoder on ONNX Runtime behind `IEmbeddingModel`.

## Role

Lets a knowledge store embed text on the host, with no provider call. Depends on `Ananke.Abstractions` only, plus
`Microsoft.ML.OnnxRuntime` and `Microsoft.ML.Tokenizers`.

## Start Here

1. `OnnxEmbeddingModel` — loads the model and vocabulary, splits text into word pieces (`BertTokenizer`), runs batches,
   pools and normalizes — `src/Ananke.Embeddings.Onnx/OnnxEmbeddingModel.cs`
2. `OnnxEmbeddingOptions` and `OnnxPooling` — where the model is and how to read it —
   `src/Ananke.Embeddings.Onnx/OnnxEmbeddingOptions.cs`

## How a batch runs

- Each text becomes `[CLS]`, its word pieces cut to `MaxTokens - 2`, and `[SEP]`.
- The batch is padded to its longest text; `attention_mask` marks the real tokens, `token_type_ids` are zero. Only the
  inputs the model declares are sent.
- A model whose first output is per token is pooled (`[CLS]`, or the mean of the real tokens); one whose output is
  already per text is taken as it is. Vectors are then scaled to length 1 unless `Normalize` is off.
- `Identity` is `onnx/<model>@<revision> (<width>)`, the width read from the output's last dimension.
