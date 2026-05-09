using Ananke.Federation.Validation;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class DeployabilityReportTests
{
    [Test]
    public void IsDeployable_true_when_no_errors()
    {
        var report = new DeployabilityReport
        {
            Diagnostics =
            [
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Warning, Code = "FED003", Message = "warn" },
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Info, Code = "FED015", Message = "info" }
            ]
        };
        report.IsDeployable.ShouldBeTrue();
    }

    [Test]
    public void IsDeployable_false_when_errors_present()
    {
        var report = new DeployabilityReport
        {
            Diagnostics =
            [
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Error, Code = "FED001", Message = "err" }
            ]
        };
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public void Ok_creates_empty_deployable_report()
    {
        var report = DeployabilityReport.Ok();
        report.IsDeployable.ShouldBeTrue();
        report.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void Errors_filters_correctly()
    {
        var report = new DeployabilityReport
        {
            Diagnostics =
            [
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Error, Code = "FED001", Message = "err" },
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Warning, Code = "FED003", Message = "warn" },
                new DeployDiagnostic { Severity = DeployDiagnosticSeverity.Error, Code = "FED020", Message = "err2" }
            ]
        };
        report.Errors.Count.ShouldBe(2);
        report.Warnings.Count.ShouldBe(1);
    }
}
