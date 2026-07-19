using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>code_execution</c> and <c>code_interpreter</c>
/// capabilities (Claude, Vertex AI / Gemini Enterprise, Foundry).
/// Delegates to <see cref="BashExecutor"/> using the appropriate interpreter
/// for the requested language.
/// </summary>
/// <remarks>
/// Supported languages: <c>python</c> / <c>python3</c>, <c>javascript</c> / <c>node</c>,
/// <c>bash</c> / <c>sh</c>, and <c>csharp</c> / <c>dotnet-script</c>.
/// The interpreter must be installed on the local machine.
/// </remarks>
internal sealed class CodeExecutionExecutor : IPlatformNativeExecutor
{
    private readonly BashExecutor _bash;
    private readonly string _capability;

    public CodeExecutionExecutor(BashExecutor bash, string capability = "code_execution")
    {
        ArgumentNullException.ThrowIfNull(bash);
        _bash = bash;
        _capability = capability;
    }

    public string Capability => _capability;
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("code", out var codeVal) || codeVal is null)
            return ToolResult.Fatal("Missing required argument: code");

        var code = codeVal.ToString()!;
        var language = args.TryGetValue("language", out var lang) ? lang?.ToString() ?? "python" : "python";

        var (interpreter, extension) = ResolveInterpreter(language);
        if (interpreter is null)
            return ToolResult.Fatal($"Unsupported language '{language}'. Supported: python, javascript, bash, csharp.");

        var scriptFile = Path.Combine(_bash.SandboxRoot, $"_script_{Guid.NewGuid():N}.{extension}");
        await File.WriteAllTextAsync(scriptFile, code, ct).ConfigureAwait(false);

        var command = $"{interpreter} \"{scriptFile}\"";
        var bashArgs = new Dictionary<string, object?> { ["command"] = command };
        return await _bash.ExecuteAsync(bashArgs, ct).ConfigureAwait(false);
    }

    private static (string? Interpreter, string Extension) ResolveInterpreter(string language) =>
        language.ToLowerInvariant() switch
        {
            "python" or "python3" => ("python3", "py"),
            "javascript" or "node" or "js" => ("node", "js"),
            "bash" or "sh" => ("bash", "sh"),
            "csharp" or "c#" or "dotnet-script" => ("dotnet-script", "csx"),
            _ => (null, "txt")
        };
}
