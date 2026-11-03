using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Ananke.Embeddings.Onnx;

/// <summary>
/// An <see cref="IEmbeddingModel"/> that runs a BERT-style encoder on ONNX Runtime, on this host. Texts are split into
/// word pieces with the model's own vocabulary, cut to <see cref="OnnxEmbeddingOptions.MaxTokens"/>, run in batches of
/// <see cref="OnnxEmbeddingOptions.BatchSize"/>, pooled and normalized as the options say.
/// </summary>
/// <remarks>
/// The model may take <c>input_ids</c>, <c>attention_mask</c> and <c>token_type_ids</c>, any of them; its first output
/// is either one vector per token, which is pooled, or one per text, which is taken as it is. Running the model is
/// synchronous: the returned tasks are complete when they are returned.
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class OnnxEmbeddingModel : IEmbeddingModel, IDisposable
{
    private static readonly string[] KnownInputs = ["input_ids", "attention_mask", "token_type_ids"];

    private readonly OnnxEmbeddingOptions _options;
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _output;

    /// <summary>Loads the model and its vocabulary from <see cref="OnnxEmbeddingOptions.ModelDirectory"/>.</summary>
    /// <exception cref="FileNotFoundException">The model file, or both <c>vocab.txt</c> and <c>tokenizer.json</c>, are missing.</exception>
    /// <exception cref="NotSupportedException">The model takes an input other than the three a BERT encoder takes.</exception>
    public OnnxEmbeddingModel(OnnxEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxTokens, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);

        var modelPath = Path.Combine(options.ModelDirectory, options.ModelFile);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"No ONNX model at '{modelPath}'.", modelPath);

        _options = options;
        _tokenizer = LoadTokenizer(options);
        _session = new InferenceSession(modelPath);

        var unknown = _session.InputMetadata.Keys.Except(KnownInputs).ToList();
        if (unknown.Count > 0)
        {
            _session.Dispose();
            throw new NotSupportedException(
                $"The model takes inputs a BERT encoder doesn't: {string.Join(", ", unknown)}.");
        }

        _output = _session.OutputMetadata.Keys.First();
        var width = _session.OutputMetadata[_output].Dimensions[^1];
        Identity = new EmbeddingModelIdentity(
            "onnx",
            options.ModelName ?? new DirectoryInfo(options.ModelDirectory).Name,
            width > 0 ? width : null,
            options.Revision);
    }

    /// <inheritdoc />
    public EmbeddingModelIdentity? Identity { get; }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        (await EmbedBatchAsync([text], ct).ConfigureAwait(false))[0];

    /// <inheritdoc />
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var batch in texts.Chunk(_options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            vectors.AddRange(Run(batch));
        }

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
    }

    /// <summary>Releases the ONNX Runtime session.</summary>
    public void Dispose() => _session.Dispose();

    /// <summary>The ids a text is read as: <c>[CLS]</c>, its word pieces cut to fit, <c>[SEP]</c>.</summary>
    internal IReadOnlyList<long> Ids(string text) =>
    [
        _tokenizer.ClassificationTokenId,
        .. _tokenizer.EncodeToIds(text, addSpecialTokens: false).Take(_options.MaxTokens - 2).Select(id => (long)id),
        _tokenizer.SeparatorTokenId,
    ];

    /// <summary>
    /// One text's vector from its tokens' vectors (<paramref name="tokens"/> rows of <paramref name="width"/>), of which
    /// the first <paramref name="length"/> are the text's and the rest are padding.
    /// </summary>
    internal static float[] Pool(ReadOnlySpan<float> tokens, int width, int length, OnnxPooling pooling, bool normalize)
    {
        var vector = new float[width];
        if (pooling == OnnxPooling.Cls)
        {
            tokens[..width].CopyTo(vector);
        }
        else
        {
            for (var t = 0; t < length; t++)
            {
                for (var d = 0; d < width; d++)
                    vector[d] += tokens[t * width + d];
            }

            for (var d = 0; d < width; d++)
                vector[d] /= length;
        }

        return normalize ? Normalized(vector) : vector;
    }

    private IEnumerable<ReadOnlyMemory<float>> Run(string[] texts)
    {
        var ids = texts.Select(Ids).ToArray();
        var length = ids.Max(i => i.Count);
        var inputIds = new DenseTensor<long>([texts.Length, length]);
        var mask = new DenseTensor<long>([texts.Length, length]);
        var types = new DenseTensor<long>([texts.Length, length]);
        for (var row = 0; row < ids.Length; row++)
        {
            for (var col = 0; col < ids[row].Count; col++)
            {
                inputIds[row, col] = ids[row][col];
                mask[row, col] = 1;
            }
        }

        var tensors = new Dictionary<string, DenseTensor<long>>
        {
            ["input_ids"] = inputIds,
            ["attention_mask"] = mask,
            ["token_type_ids"] = types,
        };
        var inputs = _session.InputMetadata.Keys.Select(name => NamedOnnxValue.CreateFromTensor(name, tensors[name])).ToList();

        using var outputs = _session.Run(inputs, [_output]);
        var output = outputs[0].AsTensor<float>();
        var values = output.ToArray();
        var vectors = new List<ReadOnlyMemory<float>>(texts.Length);
        if (output.Rank == 2)
        {
            var width = output.Dimensions[1];
            for (var row = 0; row < texts.Length; row++)
            {
                var vector = values.AsSpan(row * width, width).ToArray();
                vectors.Add(_options.Normalize ? Normalized(vector) : vector);
            }
        }
        else
        {
            var width = output.Dimensions[2];
            var rowSize = output.Dimensions[1] * width;
            for (var row = 0; row < texts.Length; row++)
                vectors.Add(Pool(values.AsSpan(row * rowSize, rowSize), width, ids[row].Count, _options.Pooling, _options.Normalize));
        }

        return vectors;
    }

    private static float[] Normalized(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0f)
            return vector;

        for (var d = 0; d < vector.Length; d++)
            vector[d] /= norm;

        return vector;
    }

    private static BertTokenizer LoadTokenizer(OnnxEmbeddingOptions options)
    {
        var bert = new BertOptions { LowerCaseBeforeTokenization = options.LowerCase };
        var vocabPath = Path.Combine(options.ModelDirectory, "vocab.txt");
        if (File.Exists(vocabPath))
            return BertTokenizer.Create(vocabPath, bert);

        var tokenizerPath = Path.Combine(options.ModelDirectory, "tokenizer.json");
        if (!File.Exists(tokenizerPath))
            throw new FileNotFoundException(
                $"No vocabulary in '{options.ModelDirectory}': neither vocab.txt nor tokenizer.json.", vocabPath);

        // tokenizer.json's model.vocab maps each word piece to its id; BertTokenizer reads one piece a line, in id order.
        using var json = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
        var pieces = json.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()
            .OrderBy(p => p.Value.GetInt32())
            .Select(p => p.Name);
        using var vocab = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', pieces)));
        return BertTokenizer.Create(vocab, bert);
    }
}
