using System.Reflection;
using ArchUnitNET.Loader;
using LifeTracker.Application.Abstractions;
using LifeTracker.Host;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.Kernel;
using LifeTracker.Operations.Application;
using LifeTracker.Operations.Domain;
using LifeTracker.Operations.Infrastructure;
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
        typeof(IUtcClock).Assembly, typeof(CurrentState).Assembly, typeof(RecordAssessmentsHandler).Assembly, typeof(RetryPolicy).Assembly, typeof(IJobQueue).Assembly];
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader().LoadAssemblies([.. Production, typeof(CoreDbContext).Assembly, typeof(SqliteStateRepository).Assembly, typeof(SqliteJobQueue).Assembly, typeof(LocalApiHost).Assembly]).Build();

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
            ["LifeTracker.Operations.Domain"] = ["LifeTracker.Kernel"],
            ["LifeTracker.Operations.Application"] = ["LifeTracker.Operations.Domain", "LifeTracker.Application.Abstractions"],
            ["LifeTracker.State.Application"] = ["LifeTracker.Kernel", "LifeTracker.Application.Abstractions", "LifeTracker.State.Domain", "LifeTracker.State.Contracts"]
        };
        foreach (var assembly in Production)
        {
            foreach (var dependency in assembly.GetReferencedAssemblies())
            {
                var name = dependency.Name!;
                if (name.StartsWith("System.", StringComparison.Ordinal) || name == "System.Runtime")
                {
                    continue;
                }

                Assert.Contains(name, allowed[assembly.GetName().Name!]);
            }
        }
    }

    [Fact]
    public void InnerLayersNeverDependOnInfrastructure()
    {
        foreach (var assembly in Production)
        {
            var rule = Types().That().ResideInAssembly(assembly).Should()
                .NotDependOnAny(Types().That().ResideInAssembly(typeof(CoreDbContext).Assembly)
                    .Or().ResideInAssembly(typeof(SqliteStateRepository).Assembly)
                    .Or().ResideInAssembly(typeof(SqliteJobQueue).Assembly)
                    .Or().ResideInAssembly(typeof(LocalApiHost).Assembly));
            Assert.True(rule.HasNoViolations(Architecture));
        }
    }

    [Fact]
    public void OperationsAndStateAdaptersRemainIndependent()
    {
        foreach (var (first, second) in new[] { (typeof(SqliteJobQueue).Assembly, typeof(SqliteStateRepository).Assembly),
            (typeof(SqliteStateRepository).Assembly, typeof(SqliteJobQueue).Assembly) })
        {
            Assert.True(Types().That().ResideInAssembly(first).Should()
                .NotDependOnAny(Types().That().ResideInAssembly(second)).HasNoViolations(Architecture));
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
