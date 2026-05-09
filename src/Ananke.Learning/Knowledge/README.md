# Ananke.Learning.Knowledge

How `Ananke.Learning` uses the graph substrate from `Ananke.Abstractions.Graph`.

The types in this folder project tag/episode/document structure into an
`IKnowledgeGraph` so that retrieval, importance tracking, and consolidation can
exploit relationships that flat or pairwise stores do not capture.

## Builders

| Type | Source | What it produces |
|---|---|---|
| `TagCoOccurrenceBuilder` | `IEmpiricalMemory` | `entry` ↔ `tag` nodes, `tagged` edges; `tag` ↔ `tag` `co_occurs` edges (Inferred) |
| `EpisodeTrajectoryBuilder` | `IEpisodeStore` | `entry` nodes, `step_of` + `follows` edges (Extracted) |
| `DocumentStructureBuilder` | hook for `ExternalKnowledgeSyncer` | `document`/`section`/`entity` nodes, `mentions`/`cites` edges |

## Retrieval

`GraphExpandedPredictionSource` — implements `IPredictionSource` by seeding with
tag-overlap neighbours, then expanding k hops through the tag graph to surface
entries that pure vector recall misses (multi-hop: tag A → tag B → tag C).

## Analytics

`GraphTagImportanceTracker` — implements `ITagImportanceTracker` via PageRank on
the tag graph.  High-frequency hub tags are penalised; bridge tags that connect
topic clusters score higher than frequency alone would suggest.

`CommunityConsolidator` — `IConsolidationSummarizer` decorator that uses
`ICommunityDetector` (when registered) to group episodes by topic cluster before
picking consolidation candidates.  Falls back to the wrapped summarizer unchanged
when no detector is registered.

## Reporting

`KnowledgeReportExporter` — writes `memory-graph.json` (full node/edge dump) and
`MEMORY_REPORT.md` (top tags, community summary) to a target directory.
