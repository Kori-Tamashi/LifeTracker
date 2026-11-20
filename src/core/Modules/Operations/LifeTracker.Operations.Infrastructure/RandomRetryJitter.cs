using LifeTracker.Operations.Application;

namespace LifeTracker.Operations.Infrastructure;

public sealed class RandomRetryJitter : IRetryJitter
{
    public double NextUnit() => Random.Shared.NextDouble();
}
