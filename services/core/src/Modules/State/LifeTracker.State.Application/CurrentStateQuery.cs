using LifeTracker.Application.Abstractions;
using LifeTracker.Kernel;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

public sealed class CurrentStateQuery(IStateRepository repository, IUtcClock clock) : IGetCurrentState
{
    public async Task<CurrentState> ExecuteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        var records = await repository.GetCurrentAsync(profileId, cancellationToken);
        var snapshot = BalancePolicy.Calculate(profileId, records, clock.UtcNow);
        var metrics = snapshot.Metrics.Select(m => new MetricState(StateValidation.MetricName(m.Metric),
            m.Assessment?.Score.Value, m.Assessment?.Source switch
            {
                AssessmentSource.SelfReport => "self_report",
                AssessmentSource.AiEstimate => "ai_estimate",
                _ => "unknown"
            }, m.Assessment?.ObservedAt,
            m.Assessment is null ? null : $"{m.Assessment.AssessmentId:D}:{m.Assessment.Revision}", m.Fresh)).ToArray();
        return new(Array.AsReadOnly(metrics), snapshot.Balance?.Value, snapshot.Level switch
        {
            BalanceLevel.Undefined => "undefined",
            BalanceLevel.Attention => "attention",
            BalanceLevel.Satisfactory => "satisfactory",
            BalanceLevel.Good => "good",
            BalanceLevel.Excellent => "excellent",
            _ => throw new InvalidOperationException("Unexpected balance level.")
        }, snapshot.ComputedAt, snapshot.ValidUntil, BalanceSnapshot.RuleVersion);
    }
}
