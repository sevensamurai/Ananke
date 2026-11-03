using System.Diagnostics.CodeAnalysis;

namespace Ananke.Embeddings.Onnx;

/// <summary>How a token-level output becomes one vector per text.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public enum OnnxPooling
{
    /// <summary>The first token's vector (<c>[CLS]</c>), as BGE models are trained to be read.</summary>
    Cls,

    /// <summary>The mean of the vectors of the tokens the attention mask keeps, as sentence-transformers models are.</summary>
    Mean,
}

/// <summary>
/// Where an <see cref="OnnxEmbeddingModel"/>'s model is, and how to read it. The directory holds the ONNX file and the
/// model's vocabulary, as <c>vocab.txt</c> or inside <c>tokenizer.json</c>. Nothing is downloaded.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record OnnxEmbeddingOptions
{
    /// <summary>The directory that holds the model and its vocabulary.</summary>
    public required string ModelDirectory { get; init; }

    /// <summary>The ONNX file in <see cref="ModelDirectory"/>. Default is <c>model.onnx</c>.</summary>
    public string ModelFile { get; init; } = "model.onnx";

    /// <summary>The model's name in its <see cref="Abstractions.Agents.EmbeddingModelIdentity"/>. Default is the directory's name.</summary>
    public string? ModelName { get; init; }

    /// <summary>The model's version or commit, for its identity.</summary>
    public string? Revision { get; init; }

    /// <summary>How the token vectors become one vector. Default is <see cref="OnnxPooling.Cls"/>.</summary>
    public OnnxPooling Pooling { get; init; } = OnnxPooling.Cls;

    /// <summary>Whether each vector is scaled to length 1. Default is <see langword="true"/>.</summary>
    public bool Normalize { get; init; } = true;

    /// <summary>Whether text is lower-cased before it is split into tokens, as uncased models need. Default is <see langword="true"/>.</summary>
    public bool LowerCase { get; init; } = true;

    /// <summary>The most tokens a text is read to, its two special tokens included; the rest is cut. Default is 512.</summary>
    public int MaxTokens { get; init; } = 512;

    /// <summary>How many texts go through the model at once. Default is 16.</summary>
    public int BatchSize { get; init; } = 16;
}
