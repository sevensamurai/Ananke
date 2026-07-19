using Ananke.Abstractions.Agents;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel.Snapshots;

[TestFixture]
public class HostSnapshotExporterTests
{
    // ── ToYaml basics ───────────────────────────────────────────────

    [Test]
    public void ToYaml_MinimalSnapshot_ContainsKernelHeader()
    {
        var snapshot = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("kernel: test-kernel");
        yaml.ShouldContain("version: 1");
        yaml.ShouldContain("taken_at:");
    }

    [Test]
    public void ToYaml_CellWithTools_ListsToolNames()
    {
        var snapshot = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("- search_catalog");
        yaml.ShouldContain("- get_book_details");
    }

    [Test]
    public void ToYaml_CellWithModel_IncludesProviderAndModel()
    {
        var snapshot = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("provider: openai");
        yaml.ShouldContain($"model: {Models.OpenAI.Gpt54Mini}");
    }

    [Test]
    public void ToYaml_CellWithMemory_IncludesDomainsAndLineage()
    {
        var snapshot = BuildSnapshotWithMemory();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("domains: [catalog, general]");
        yaml.ShouldContain("lineage: [bookstore]");
    }

    [Test]
    public void ToYaml_WithRouting_IncludesRoutingTable()
    {
        var snapshot = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("routing:");
        yaml.ShouldContain("catalog: bookstore-catalog");
        yaml.ShouldContain("orders: bookstore-orders");
    }

    [Test]
    public void ToYaml_WithHistory_IncludesDivisionRecord()
    {
        var snapshot = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("history:");
        yaml.ShouldContain("parent: bookstore-general");
        yaml.ShouldContain("children: [bookstore-catalog, bookstore-orders]");
    }

    [Test]
    public void ToYaml_DividedFrom_IncludesLineage()
    {
        var snapshot = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(snapshot);

        yaml.ShouldContain("divided_from: bookstore-general");
    }

    [Test]
    public void ToYaml_NullSnapshot_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            HostSnapshotExporter.ToYaml(null!));
    }

    // ── Round-trip (ToYaml → FromYaml) ──────────────────────────────

    [Test]
    public void RoundTrip_MinimalSnapshot_PreservesKernelId()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.KernelId.ShouldBe(original.KernelId);
        restored.Version.ShouldBe(original.Version);
    }

    [Test]
    public void RoundTrip_CellTools_Preserved()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.Cells.Count.ShouldBe(1);
        restored.Cells[0].Tools.ShouldBe(original.Cells[0].Tools);
    }

    [Test]
    public void RoundTrip_CellDomain_Preserved()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.Cells[0].Domain.ShouldBe("bookstore");
    }

    [Test]
    public void RoundTrip_Models_Preserved()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        var model = restored.Cells[0].Models["default"];
        model.Provider.ShouldBe("openai");
        model.Model.ShouldBe(Models.OpenAI.Gpt54Mini);
    }

    [Test]
    public void RoundTrip_Jobs_Preserved()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        var job = restored.Cells[0].Jobs["handle-request"];
        job.Type.ShouldBe("agent");
        job.ModelAlias.ShouldBe("default");
    }

    [Test]
    public void RoundTrip_Connections_Preserved()
    {
        var original = BuildMinimalSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.Cells[0].Connections.ShouldBe(original.Cells[0].Connections);
    }

    [Test]
    public void RoundTrip_MemoryProfile_Preserved()
    {
        var original = BuildSnapshotWithMemory();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        var mem = restored.Cells[0].MemoryProfile;
        mem.ShouldNotBeNull();
        mem.Domains.ShouldBe(["catalog", "general"]);
        mem.LineageTags.ShouldBe(["bookstore"]);
    }

    [Test]
    public void RoundTrip_PostDivisionSnapshot_PreservesAllCells()
    {
        var original = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.Cells.Count.ShouldBe(2);
        restored.Cells[0].Name.ShouldBe("bookstore-catalog");
        restored.Cells[1].Name.ShouldBe("bookstore-orders");
    }

    [Test]
    public void RoundTrip_RoutingTable_Preserved()
    {
        var original = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.RoutingTable["catalog"].ShouldBe("bookstore-catalog");
        restored.RoutingTable["orders"].ShouldBe("bookstore-orders");
    }

    [Test]
    public void RoundTrip_DivisionHistory_Preserved()
    {
        var original = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.DivisionHistory.Count.ShouldBe(1);
        var record = restored.DivisionHistory[0];
        record.ParentWorkflow.ShouldBe("bookstore-general");
        record.Children.ShouldBe(["bookstore-catalog", "bookstore-orders"]);
        record.ApprovedBy.ShouldBe("operator");
    }

    [Test]
    public void RoundTrip_DividedFrom_Preserved()
    {
        var original = BuildPostDivisionSnapshot();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        restored.Cells[0].SplitFrom.ShouldBe("bookstore-general");
    }

    [Test]
    public void RoundTrip_SystemPrompt_Preserved()
    {
        var original = BuildSnapshotWithSystemPrompt();

        var yaml = HostSnapshotExporter.ToYaml(original);
        var restored = HostSnapshotExporter.FromYaml(yaml);

        var job = restored.Cells[0].Jobs["handle-request"];
        job.SystemPrompt.ShouldNotBeNull();
        job.SystemPrompt.ShouldContain("You are a bookstore catalog assistant");
    }

    // ── FromYaml error handling ─────────────────────────────────────

    [Test]
    public void FromYaml_EmptyString_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            HostSnapshotExporter.FromYaml(""));
    }

    [Test]
    public void FromYaml_MissingKernelField_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            HostSnapshotExporter.FromYaml("version: 1"));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedTime =
        new(2025, 1, 15, 14, 30, 0, TimeSpan.Zero);

    private static HostSnapshot BuildMinimalSnapshot() => new()
    {
        KernelId = "test-kernel",
        Version = 1,
        TakenAt = FixedTime,
        Cells =
        [
            new WorkflowSnapshot
            {
                Name = "bookstore-general",
                Domain = "bookstore",
                Tools = ["search_catalog", "get_book_details", "check_inventory"],
                Connections = ["handle-request -> respond", "respond -> End"],
                Jobs = new Dictionary<string, JobSnapshot>
                {
                    ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
                    ["respond"] = new() { Type = "code" }
                },
                Models = new Dictionary<string, ModelSnapshot>
                {
                    ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
                }
            }
        ]
    };

    private static HostSnapshot BuildSnapshotWithMemory() => new()
    {
        KernelId = "test-kernel",
        Version = 1,
        TakenAt = FixedTime,
        Cells =
        [
            new WorkflowSnapshot
            {
                Name = "bookstore-catalog",
                Domain = "catalog",
                SplitFrom = "bookstore-general",
                Tools = ["search_catalog"],
                Connections = ["handle-request -> End"],
                Jobs = new Dictionary<string, JobSnapshot>
                {
                    ["handle-request"] = new() { Type = "agent", ModelAlias = "default" }
                },
                Models = new Dictionary<string, ModelSnapshot>
                {
                    ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
                },
                MemoryProfile = new MemoryProfile
                {
                    Domains = ["catalog", "general"],
                    LineageTags = ["bookstore"]
                }
            }
        ]
    };

    private static HostSnapshot BuildPostDivisionSnapshot() => new()
    {
        KernelId = "bookstore",
        Version = 2,
        TakenAt = FixedTime,
        Cells =
        [
            new WorkflowSnapshot
            {
                Name = "bookstore-catalog",
                Domain = "catalog",
                SplitFrom = "bookstore-general",
                Tools = ["search_catalog", "get_book_details", "check_inventory", "get_recommendations"],
                Connections = ["handle-request -> respond"],
                Jobs = new Dictionary<string, JobSnapshot>
                {
                    ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
                    ["respond"] = new() { Type = "code" }
                },
                Models = new Dictionary<string, ModelSnapshot>
                {
                    ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
                },
                MemoryProfile = new MemoryProfile
                {
                    Domains = ["catalog", "general"],
                    LineageTags = ["bookstore"]
                }
            },
            new WorkflowSnapshot
            {
                Name = "bookstore-orders",
                Domain = "orders",
                SplitFrom = "bookstore-general",
                Tools = ["process_payment", "create_order", "track_shipment"],
                Connections = ["handle-request -> respond"],
                Jobs = new Dictionary<string, JobSnapshot>
                {
                    ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
                    ["respond"] = new() { Type = "code" }
                },
                Models = new Dictionary<string, ModelSnapshot>
                {
                    ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
                },
                MemoryProfile = new MemoryProfile
                {
                    Domains = ["orders", "general"],
                    LineageTags = ["bookstore"]
                }
            }
        ],
        RoutingTable = new Dictionary<string, string>
        {
            ["catalog"] = "bookstore-catalog",
            ["orders"] = "bookstore-orders"
        },
        DivisionHistory =
        [
            new DivisionRecord
            {
                ParentWorkflow = "bookstore-general",
                Children = ["bookstore-catalog", "bookstore-orders"],
                Reason = "Surface tension exceeded thresholds",
                OccurredAt = FixedTime.AddMinutes(-1),
                ApprovedBy = "operator"
            }
        ]
    };

    private static HostSnapshot BuildSnapshotWithSystemPrompt() => new()
    {
        KernelId = "test-kernel",
        Version = 1,
        TakenAt = FixedTime,
        Cells =
        [
            new WorkflowSnapshot
            {
                Name = "bookstore-catalog",
                Domain = "catalog",
                Tools = ["search_catalog"],
                Connections = ["handle-request -> End"],
                Jobs = new Dictionary<string, JobSnapshot>
                {
                    ["handle-request"] = new()
                    {
                        Type = "agent",
                        ModelAlias = "default",
                        SystemPrompt = "You are a bookstore catalog assistant.\nHelp users find books."
                    }
                },
                Models = new Dictionary<string, ModelSnapshot>
                {
                    ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
                }
            }
        ]
    };
}
