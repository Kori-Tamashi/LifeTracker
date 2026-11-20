using LifeTracker.State.Domain;
using Xunit;

namespace LifeTracker.State.Domain.Tests;

public sealed class BalanceTests
{
    private static readonly Guid Profile = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T12:00:00Z");
    private static AssessmentRevision Entry(StateMetric metric, decimal score = 8, DateTimeOffset? at = null,
        AssessmentSource source = AssessmentSource.SelfReport, Guid? id = null, int revision = 1,
        AssessmentStatus status = AssessmentStatus.Accepted, Guid? profile = null) =>
        new(profile ?? Profile, id ?? Guid.NewGuid(), revision, metric, new(score), at ?? Now, Now, source, status);
    private static AssessmentRevision[] Complete(decimal normalized) => Enum.GetValues<StateMetric>()
        .Select(m => Entry(m, m == StateMetric.Anxiety ? 10 - normalized : normalized)).ToArray();

    [Fact]
    public void EveryHalfPointIsRepresentableAndInversionIsInvolutive()
    {
        for (var half = 0; half <= 20; half++)
        {
            var score = new Score(half / 2m);
            Assert.Equal(half / 2m, score.Value);
            Assert.Equal(score, score.Invert().Invert());
        }
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(10.5)]
    [InlineData(7.25)]
    public void InvalidScoresAreRejected(double value) => Assert.Throws<ArgumentOutOfRangeException>(() => new Score((decimal)value));

    [Theory]
    [InlineData(0, BalanceLevel.Attention)]
    [InlineData(5.5, BalanceLevel.Attention)]
    [InlineData(6, BalanceLevel.Satisfactory)]
    [InlineData(7, BalanceLevel.Satisfactory)]
    [InlineData(7.5, BalanceLevel.Good)]
    [InlineData(8.5, BalanceLevel.Good)]
    [InlineData(9, BalanceLevel.Excellent)]
    [InlineData(10, BalanceLevel.Excellent)]
    public void ThresholdsAreInclusive(double value, BalanceLevel expected)
    {
        var result = BalancePolicy.Calculate(Profile, Complete((decimal)value), Now);
        Assert.Equal((decimal)value, result.Balance!.Value.Value);
        Assert.Equal(expected, result.Level);
        Assert.Equal(Now.AddHours(6), result.ValidUntil);
    }

    [Fact]
    public void AnxietyIsInvertedAndWeakestMetricDeterminesBalance()
    {
        var entries = new[] { Entry(StateMetric.Energy, 8), Entry(StateMetric.Mood, 7.5m),
            Entry(StateMetric.Anxiety, 2), Entry(StateMetric.PhysicalWellbeing, 9) };
        Assert.Equal(7.5m, BalancePolicy.Calculate(Profile, entries, Now).Balance!.Value.Value);
    }

    [Fact]
    public void MissingValuesNeverBecomeInventedScores()
    {
        var result = BalancePolicy.Calculate(Profile, [Entry(StateMetric.Energy)], Now);
        Assert.Null(result.Balance);
        Assert.Null(result.ValidUntil);
        Assert.Equal(BalanceLevel.Undefined, result.Level);
        Assert.Equal(4, result.Metrics.Count);
        Assert.Equal(3, result.Metrics.Count(m => m.Assessment is null));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void SixHourBoundaryIsExclusive(int seconds, bool fresh)
    {
        var entry = Entry(StateMetric.Energy, at: Now.AddHours(-6).AddSeconds(-seconds));
        var result = BalancePolicy.Calculate(Profile, [entry], Now);
        Assert.Equal(fresh, result.Metrics.Single(m => m.Metric == StateMetric.Energy).Fresh);
        Assert.Same(entry, result.Metrics.Single(m => m.Metric == StateMetric.Energy).Assessment);
    }

    [Fact]
    public void FreshManualOverridesNewerAiButExpiredManualDoesNot()
    {
        var manual = Entry(StateMetric.Energy, 4, Now.AddHours(-5));
        var ai = Entry(StateMetric.Energy, 9, Now, AssessmentSource.AiEstimate);
        Assert.Same(manual, BalancePolicy.Calculate(Profile, [ai, manual], Now).Metrics[0].Assessment);
        Assert.Same(ai, BalancePolicy.Calculate(Profile, [ai, manual], Now.AddHours(1)).Metrics[0].Assessment);
    }

    [Fact]
    public void FutureAndUnacceptedDataCannotAffectBalance()
    {
        var result = BalancePolicy.Calculate(Profile, [Entry(StateMetric.Energy, at: Now.AddTicks(1)),
            Entry(StateMetric.Mood, status: AssessmentStatus.Rejected)], Now);
        Assert.All(result.Metrics, m => Assert.Null(m.Assessment));
    }

    [Theory]
    [InlineData(AssessmentStatus.Rejected)]
    [InlineData(AssessmentStatus.Deleted)]
    public void SupersededRevisionNeverReappears(AssessmentStatus status)
    {
        var id = Guid.NewGuid();
        var old = Entry(StateMetric.Energy, id: id);
        var updated = Entry(StateMetric.Energy, id: id, revision: 2, status: status);
        Assert.Null(BalancePolicy.Calculate(Profile, [updated, old], Now).Metrics[0].Assessment);
    }

    [Fact]
    public void LatestRevisionAndDeterministicTieBreakerIgnoreInputOrder()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var entries = new[] { Entry(StateMetric.Energy, 1, id: low), Entry(StateMetric.Energy, 2, id: high),
            Entry(StateMetric.Energy, 3, id: high, revision: 2) };
        var selected = BalancePolicy.Calculate(Profile, entries.Reverse(), Now).Metrics[0].Assessment;
        Assert.Equal(high, selected!.AssessmentId);
        Assert.Equal(3m, selected.Score.Value);
    }

    [Fact]
    public void ConflictingCopiesAndForeignProfilesFailClosed()
    {
        var entry = Entry(StateMetric.Energy);
        Assert.Throws<InvalidOperationException>(() => BalancePolicy.Calculate(Profile,
            [entry, Entry(StateMetric.Energy, 1, id: entry.AssessmentId)], Now));
        Assert.Throws<InvalidOperationException>(() => BalancePolicy.Calculate(Profile,
            [Entry(StateMetric.Energy, profile: Guid.NewGuid())], Now));
        Assert.NotNull(BalancePolicy.Calculate(Profile, [entry, entry], Now));
    }
}
