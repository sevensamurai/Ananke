using Ananke.Tool.Shared;
using Shouldly;

namespace Ananke.Tool.Tests;

[TestFixture]
public class ProjectNameValidatorTests
{
    [TestCase("my-project")]
    [TestCase("my_project")]
    [TestCase("MyProject42")]
    [TestCase("v1.2.3")]
    public void IsValid_AllowlistedName_ReturnsTrue(string name) =>
        ProjectNameValidator.IsValid(name).ShouldBeTrue();

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("bad<>|name")]
    [TestCase("has/slash")]
    [TestCase("has space")]
    [TestCase("café")]
    public void IsValid_RejectedName_ReturnsFalse(string? name) =>
        ProjectNameValidator.IsValid(name).ShouldBeFalse();
}
