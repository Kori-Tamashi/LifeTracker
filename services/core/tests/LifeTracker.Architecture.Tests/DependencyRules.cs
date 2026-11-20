using System.Reflection;
using ArchUnitNET.Loader;
using LifeTracker.Application.Abstractions;
using LifeTracker.Kernel;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;
using LifeTracker.State.Infrastructure;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace LifeTracker.Architecture.Tests;

public sealed class DependencyRules
{
    private static readonly Assembly[] Production = [typeof(Score).Assembly, typeof(Identifiers).Assembly,
        typeof(IUtcClock).Assembly, typeof(CurrentState).Assembly, typeof(RecordAssessmentsHandler).Assembly];
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader().LoadAssemblies([.. Production, typeof(StateDbContext).Assembly]).Build();

    [Fact]
    public void DomainCannotDependOnApplicationOrContracts()
    {
        var rule = Types().That().ResideInAssembly(typeof(Score).Assembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(typeof(RecordAssessmentsHandler).Assembly)
                .Or().ResideInAssembly(typeof(CurrentState).Assembly));
        Assert.True(rule.HasNoViolations(Architecture));
    }

    [Fact]
    public void KernelAndContractsRemainIndependentOfOuterLayers()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["LifeTracker.Kernel"] = [],
            ["LifeTracker.Application.Abstractions"] = [],
            ["LifeTracker.State.Contracts"] = [],
            ["LifeTracker.State.Domain"] = ["LifeTracker.Kernel"],
            ["LifeTracker.State.Application"] = ["LifeTracker.Kernel", "LifeTracker.Application.Abstractions", "LifeTracker.State.Domain", "LifeTracker.State.Contracts"]
        };
        foreach (var assembly in Production)
            foreach (var dependency in assembly.GetReferencedAssemblies())
            {
                var name = dependency.Name!;
                if (name.StartsWith("System.", StringComparison.Ordinal) || name == "System.Runtime") continue;
                Assert.Contains(name, allowed[assembly.GetName().Name!]);
            }
    }

    [Fact]
    public void InnerLayersNeverDependOnInfrastructure()
    {
        foreach (var assembly in Production)
        {
            var rule = Types().That().ResideInAssembly(assembly).Should()
                .NotDependOnAny(Types().That().ResideInAssembly(typeof(StateDbContext).Assembly));
            Assert.True(rule.HasNoViolations(Architecture));
        }
    }

    [Fact]
    public void ForbiddenDependencyFixtureReallyFailsTheRule()
    {
        var architecture = new ArchLoader().LoadAssemblies(typeof(ForbiddenDependencyFixture).Assembly, typeof(CurrentState).Assembly).Build();
        var rule = Types().That().HaveFullName(typeof(ForbiddenDependencyFixture).FullName!)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(typeof(CurrentState).Assembly));
        Assert.False(rule.HasNoViolations(architecture));
    }
}

// Deliberately invalid test-only layer: ensures a vacuous architecture rule cannot pass unnoticed.
public sealed class ForbiddenDependencyFixture
{
    public CurrentState? OuterLayer { get; set; }
}
