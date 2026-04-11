namespace Ananke.Tool.Diagnostics;

/// <summary>
/// Registry of detailed explanations for every known diagnostic code.
/// Used by <c>nnke explain &lt;code&gt;</c> to give agents and humans
/// actionable context for a specific error.
/// </summary>
internal static class DiagnosticExplanations
{
    private static readonly Dictionary<string, DiagnosticExplanation> Registry = BuildRegistry();

    /// <summary>
    /// Looks up a detailed explanation by error code (case-insensitive).
    /// </summary>
    public static DiagnosticExplanation? Find(string code) =>
        Registry.GetValueOrDefault(code.ToUpperInvariant());

    /// <summary>
    /// Returns all registered explanations, ordered by code.
    /// </summary>
    public static IReadOnlyList<DiagnosticExplanation> All() =>
        Registry.Values.OrderBy(e => e.Code).ToList();

    private static Dictionary<string, DiagnosticExplanation> BuildRegistry()
    {
        var entries = new DiagnosticExplanation[]
        {
            // ── Manifest ─────────────────────────────────────────────
            new()
            {
                Code = DiagnosticCodes.ManifestParseError,
                Title = "Manifest YAML parse error",
                Description = """
                    The .ananke.yml file could not be parsed. This usually means the
                    YAML structure is malformed — incorrect indentation, missing colons,
                    or unsupported YAML features. Ananke uses a minimal YAML parser that
                    supports only the manifest schema (mappings, lists, block scalars).
                    """,
                BadExample = """
                    name: my-workflow
                    models:
                    default:          # ← missing 2-space indent
                        provider: openai
                    """,
                FixExample = """
                    name: my-workflow
                    models:
                      default:        # ← correct 2-space indent
                        provider: openai
                        model: gpt-4.1-mini
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },
            new()
            {
                Code = DiagnosticCodes.ManifestMissingName,
                Title = "Missing required 'name:' field",
                Description = """
                    Every .ananke.yml manifest must start with a 'name:' field that
                    identifies the workflow. This name is used as the workflow identifier
                    in scaffold parsing, diagram generation, and logging.
                    """,
                BadExample = """
                    models:
                      default:
                        provider: openai
                    """,
                FixExample = """
                    name: my-workflow
                    models:
                      default:
                        provider: openai
                    """,
                DocsRef = "nnke docs design-tooling",
            },

            // ── Topology ─────────────────────────────────────────────
            new()
            {
                Code = DiagnosticCodes.UnreachableJob,
                Title = "Unreachable job",
                Description = """
                    A job is declared in the topology but has no incoming connection.
                    Every job except the entry job (the first job in the first connection)
                    must be reachable from at least one other job. An unreachable job will
                    never execute.
                    """,
                BadExample = """
                    connections:
                      - plan -> execute
                      - execute -> End
                      - orphan -> End       # 'orphan' has no incoming edge
                    """,
                FixExample = """
                    connections:
                      - plan -> execute
                      - execute -> orphan   # now reachable from 'execute'
                      - orphan -> End
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },
            new()
            {
                Code = DiagnosticCodes.MissingTerminal,
                Title = "No terminal connection (missing path to End)",
                Description = """
                    A job has no outgoing connection and does not connect to End.
                    Every path through the workflow must eventually reach the End
                    terminal. A job with no outgoing edge creates a dead end where
                    execution gets stuck.
                    """,
                BadExample = """
                    connections:
                      - start -> process
                      # 'process' has no outgoing connection
                    """,
                FixExample = """
                    connections:
                      - start -> process
                      - process -> End
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },
            new()
            {
                Code = DiagnosticCodes.ForkWithoutJoin,
                Title = "Fork without matching join",
                Description = """
                    A fork() dispatches work to parallel branches, but no join()
                    collects their results. The forked branches run independently
                    and their state changes are lost because there is no merge point.
                    Add a join() with the forked branch names and a target job where
                    the merged state continues.
                    """,
                BadExample = """
                    connections:
                      - plan -> fork(fetch_a, fetch_b)
                      - fetch_a -> transform
                      - fetch_b -> transform
                      - transform -> End
                    """,
                FixExample = """
                    connections:
                      - plan -> fork(fetch_a, fetch_b)
                      - fetch_a -> transform
                      - fetch_b -> transform
                      - join(fetch_a, fetch_b) -> transform
                      - transform -> End
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },
            new()
            {
                Code = DiagnosticCodes.UndefinedJob,
                Title = "Undefined job referenced in connection",
                Description = """
                    A connection line references a job name that is not declared in
                    the jobs: section of the manifest. This can be a typo, a renamed
                    job, or a missing job declaration. The topology parser requires
                    every name in a connection to correspond to a declared job.
                    """,
                BadExample = """
                    jobs:
                      start:
                        type: code
                    connections:
                      - start -> proccess   # typo: 'proccess' not declared
                      - proccess -> End
                    """,
                FixExample = """
                    jobs:
                      start:
                        type: code
                      process:              # add the missing job
                        type: agent
                        model: default
                    connections:
                      - start -> process
                      - process -> End
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },
            new()
            {
                Code = DiagnosticCodes.TopologyInvalid,
                Title = "General topology validation error",
                Description = """
                    The topology is invalid but the specific issue does not match a
                    more specific diagnostic code. Check the error message for details.
                    Common causes: duplicate connections, self-loops without proper
                    loop syntax, or empty connection lists.
                    """,
                BadExample = """
                    connections:
                      - start ->            # incomplete connection
                    """,
                FixExample = """
                    connections:
                      - start -> process
                      - process -> End
                    """,
                DocsRef = "nnke docs workflow-dsl",
            },

            // ── Model ────────────────────────────────────────────────
            new()
            {
                Code = DiagnosticCodes.UndefinedModelAlias,
                Title = "Undefined model alias",
                Description = """
                    An agent job references a model alias (via the 'model:' field) that
                    is not declared in the 'models:' section of the manifest. Every
                    agent job must reference a model alias that maps to a provider
                    and model name. This alias is resolved at runtime to an IAgentModel
                    instance via ModelResolver.
                    """,
                BadExample = """
                    models:
                      default:
                        provider: openai
                        model: gpt-4.1-mini
                    jobs:
                      analyze:
                        type: agent
                        model: analyst      # 'analyst' not in models:
                    """,
                FixExample = """
                    models:
                      default:
                        provider: openai
                        model: gpt-4.1-mini
                      analyst:              # add the missing alias
                        provider: openai
                        model: gpt-4.1-mini
                    jobs:
                      analyze:
                        type: agent
                        model: analyst
                    """,
                DocsRef = "nnke docs design-tooling",
            },
        };

        return entries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
    }
}
