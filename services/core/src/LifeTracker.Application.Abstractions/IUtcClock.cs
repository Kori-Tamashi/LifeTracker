namespace LifeTracker.Application.Abstractions;

public interface IUtcClock
{
    DateTimeOffset UtcNow { get; }
}
