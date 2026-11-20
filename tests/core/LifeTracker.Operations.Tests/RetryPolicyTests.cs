using LifeTracker.Operations.Domain;
using Xunit;

namespace LifeTracker.Operations.Tests;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 5000)]
    [InlineData(2, 10000)]
    [InlineData(3, 20000)]
    [InlineData(4, 40000)]
    public void TransientRetryUsesFullJitterBounds(int attempt, int cap)
    {
        Assert.Equal(TimeSpan.Zero, RetryPolicy.Delay(attempt, JobFailureKind.Timeout, 0));
        Assert.Equal(TimeSpan.FromMilliseconds(cap / 2), RetryPolicy.Delay(attempt, JobFailureKind.Unavailable, 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(cap), RetryPolicy.Delay(attempt, JobFailureKind.Timeout, 1));
    }
    [Theory]
    [InlineData(JobFailureKind.Validation)]
    [InlineData(JobFailureKind.Authorization)]
    [InlineData(JobFailureKind.Conflict)]
    [InlineData(JobFailureKind.Terminal)]
    public void PermanentFailuresDoNotRetry(JobFailureKind kind) => Assert.Null(RetryPolicy.Delay(1, kind, 0.5));
    [Fact]
    public void FiveAttemptsAreTheAutomaticLimit()
    {
        Assert.Null(RetryPolicy.Delay(5, JobFailureKind.Timeout, 1));
        Assert.Null(RetryPolicy.Delay(int.MaxValue, JobFailureKind.Timeout, 1));
    }
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidJitterIsRejected(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Delay(1, JobFailureKind.Timeout, value));
}
