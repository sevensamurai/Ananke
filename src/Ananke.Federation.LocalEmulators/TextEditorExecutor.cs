using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>text_editor</c> capability (Claude).
/// Provides view/create/str_replace/insert operations scoped to the same
/// sandbox directory used by <see cref="BashExecutor"/>.
/// </summary>
internal sealed class TextEditorExecutor : IPlatformNativeExecutor
{
    private readonly string _sandboxRoot;

    public TextEditorExecutor(string sandboxRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxRoot);
        _sandboxRoot = sandboxRoot;
        Directory.CreateDirectory(_sandboxRoot);
    }

    public string Capability => "text_editor";
    public bool IsStub => false;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("command", out var cmdVal) || cmdVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: command"));

        var command = cmdVal.ToString()!;

        return command switch
        {
            "view" => View(args),
            "create" => Create(args),
            "str_replace" => StrReplace(args),
            "insert" => Insert(args),
            _ => Task.FromResult(ToolResult.Fatal($"Unknown text_editor command: '{command}'"))
        };
    }

    private Task<ToolResult> View(IReadOnlyDictionary<string, object?> args)
    {
        if (!TryResolvePath(args, "path", out var path, out var error))
            return Task.FromResult(ToolResult.Fatal(error!));

        if (!File.Exists(path))
            return Task.FromResult(ToolResult.Error($"File not found: {path}"));

        var content = File.ReadAllText(path);
        return Task.FromResult(ToolResult.Ok(content));
    }

    private Task<ToolResult> Create(IReadOnlyDictionary<string, object?> args)
    {
        if (!TryResolvePath(args, "path", out var path, out var error))
            return Task.FromResult(ToolResult.Fatal(error!));

        var fileText = args.TryGetValue("file_text", out var ft) ? ft?.ToString() ?? string.Empty : string.Empty;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, fileText);
        return Task.FromResult(ToolResult.Ok($"Created {path}"));
    }

    private Task<ToolResult> StrReplace(IReadOnlyDictionary<string, object?> args)
    {
        if (!TryResolvePath(args, "path", out var path, out var error))
            return Task.FromResult(ToolResult.Fatal(error!));

        if (!File.Exists(path))
            return Task.FromResult(ToolResult.Error($"File not found: {path}"));

        if (!args.TryGetValue("old_str", out var oldVal) || oldVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: old_str"));

        var newStr = args.TryGetValue("new_str", out var nv) ? nv?.ToString() ?? string.Empty : string.Empty;
        var content = File.ReadAllText(path);
        var oldStr = oldVal.ToString()!;

        if (!content.Contains(oldStr, StringComparison.Ordinal))
            return Task.FromResult(ToolResult.Error($"str_replace: old_str not found in {path}"));

        File.WriteAllText(path, content.Replace(oldStr, newStr, StringComparison.Ordinal));
        return Task.FromResult(ToolResult.Ok($"Replaced in {path}"));
    }

    private Task<ToolResult> Insert(IReadOnlyDictionary<string, object?> args)
    {
        if (!TryResolvePath(args, "path", out var path, out var error))
            return Task.FromResult(ToolResult.Fatal(error!));

        if (!File.Exists(path))
            return Task.FromResult(ToolResult.Error($"File not found: {path}"));

        if (!args.TryGetValue("insert_line", out var lineVal) || lineVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: insert_line"));

        if (!args.TryGetValue("new_str", out var newVal) || newVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: new_str"));

        var lines = new List<string>(File.ReadAllLines(path));
        var lineNumber = Convert.ToInt32(lineVal);
        var insertAt = Math.Clamp(lineNumber, 0, lines.Count);
        lines.Insert(insertAt, newVal.ToString()!);
        File.WriteAllLines(path, lines);
        return Task.FromResult(ToolResult.Ok($"Inserted at line {insertAt} in {path}"));
    }

    /// <summary>
    /// Resolves <paramref name="key"/> to an absolute path under <see cref="_sandboxRoot"/>,
    /// rejecting any relative path (e.g. <c>../../etc/passwd</c>) that would normalise outside it.
    /// </summary>
    private bool TryResolvePath(
        IReadOnlyDictionary<string, object?> args,
        string key,
        out string path,
        out string? error)
    {
        path = string.Empty;

        if (!args.TryGetValue(key, out var val) || val is null)
        {
            error = $"Missing required argument: {key}";
            return false;
        }

        var relative = val.ToString()!.TrimStart('/', '\\');
        var resolved = Path.GetFullPath(Path.Combine(_sandboxRoot, relative));
        var root = Path.GetFullPath(_sandboxRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!resolved.Equals(root, comparison) &&
            !resolved.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            error = $"Path '{val}' escapes the sandbox root";
            return false;
        }

        path = resolved;
        error = null;
        return true;
    }
}
