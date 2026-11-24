using LifeTracker.Application.Abstractions;
using LifeTracker.Journal.Application;
using LifeTracker.Journal.Domain;
using Xunit;

namespace LifeTracker.Journal.Tests;

public sealed class ReviewExtractionTests
{
    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow => ExtractionTests.Now; }
    private sealed class Store(ExtractionRevision initial) : IExtractionReviewRepository
    {
        public ExtractionRevision Current = initial;
        public readonly List<ExtractionRevision> History = [initial];
        private readonly Dictionary<(Guid Profile, Guid Extraction, string Key), (string Fingerprint, ExtractionReviewReceipt Receipt)> receipts = [];
        public Action? AfterFind;
        public int Reads;
        public int Commits;
        public Task<ExtractionReviewReceipt?> FindReceiptAsync(Guid profile, Guid extraction, string key, string fingerprint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (receipts)
            {
                if (!receipts.TryGetValue((profile, extraction, key), out var existing)) { return Task.FromResult<ExtractionReviewReceipt?>(null); }
                if (existing.Fingerprint != fingerprint) { throw new InvalidOperationException("Idempotency conflict."); }
                return Task.FromResult<ExtractionReviewReceipt?>(existing.Receipt);
            }
        }
        public Task<ExtractionRevision?> FindAsync(Guid profile, Guid id, CancellationToken ct)
        {
            Reads++;
            var result = profile == Current.ProfileId && id == Current.Id ? Current : null;
            AfterFind?.Invoke();
            return Task.FromResult(result);
        }
        public Task<ExtractionReviewReceipt> CommitAsync(ExtractionReviewCommit commit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (receipts)
            {
                if (receipts.TryGetValue((commit.ProfileId, commit.Updated.Id, commit.IdempotencyKey), out var previous))
                {
                    if (previous.Fingerprint != commit.Fingerprint) { throw new InvalidOperationException("Idempotency conflict."); }
                    return Task.FromResult(previous.Receipt);
                }
                if (Current.Revision != commit.ExpectedRevision) { throw new InvalidOperationException("Revision conflict."); }
                Current = commit.Updated;
                History.Add(Current);
                receipts.Add((commit.ProfileId, Current.Id, commit.IdempotencyKey), (commit.Fingerprint, commit.Receipt));
                Commits++;
                return Task.FromResult(commit.Receipt);
            }
        }
    }

    private static ReviewExtractionCommand Command(ExtractionRevision proposal, ExtractionDecision decision = ExtractionDecision.Confirm) =>
        new(proposal.ProfileId, Guid.NewGuid(), "synthetic-review-key", proposal.Id, 1, decision);

    [Fact]
    public async Task ConfirmAppendsRevisionAndRetryReturnsOriginalReceiptWithoutAnotherWrite()
    {
        var proposal = ExtractionTests.Proposal();
        var store = new Store(proposal);
        var handler = new ReviewExtractionHandler(store, new Clock());
        var command = Command(proposal);
        var first = await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);
        var replay = await handler.ExecuteAsync(command with { CommandId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        Assert.Equal(first, replay);
        Assert.Equal(1, store.Commits);
        Assert.Equal(2, store.History.Count);
        Assert.Equal(ExtractionAcceptance.Pending, store.History[0].Acceptance);
        Assert.True(store.Current.EligibleForMemory);
    }

    [Fact]
    public async Task ReusedKeyWithDifferentDecisionCannotOverwriteAcceptedResult()
    {
        var proposal = ExtractionTests.Proposal();
        var store = new Store(proposal);
        var handler = new ReviewExtractionHandler(store, new Clock());
        var command = Command(proposal);
        await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(command with { Decision = ExtractionDecision.Reject }, TestContext.Current.CancellationToken));
        Assert.Equal(1, store.Commits);
        Assert.True(store.Current.EligibleForMemory);
    }

    [Fact]
    public async Task EditKeepsHypothesisAndPolarityAndReplayCanonicalizesCandidateOrder()
    {
        var proposal = ExtractionTests.Proposal();
        var store = new Store(proposal);
        var handler = new ReviewExtractionHandler(store, new Clock());
        var candidates = new[] { ExtractionTests.Candidate(ExtractionKind.Hypothesis), ExtractionTests.Candidate() };
        var command = Command(proposal, ExtractionDecision.Edit) with { EditedCandidates = candidates };
        var receipt = await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(receipt, await handler.ExecuteAsync(command with { EditedCandidates = candidates.Reverse().ToArray() }, TestContext.Current.CancellationToken));
        Assert.Equal(ExtractionKind.Hypothesis, store.Current.Candidates[0].Kind);
        Assert.Equal(ClaimPolarity.Negated, store.Current.Candidates[0].Polarity);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task CrossProfileNotFoundAndCancellationCannotWrite()
    {
        var proposal = ExtractionTests.Proposal();
        var store = new Store(proposal);
        var handler = new ReviewExtractionHandler(store, new Clock());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.ExecuteAsync(Command(proposal) with { ProfileId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(Command(proposal), new CancellationToken(true)));
        Assert.Equal(0, store.Commits);
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task ConcurrentReviewConflictCannotReplaceNewerDecision()
    {
        var proposal = ExtractionTests.Proposal();
        var store = new Store(proposal);
        store.AfterFind = () => store.Current = proposal.Decide(1, ExtractionDecision.Reject, ExtractionTests.Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReviewExtractionHandler(store, new Clock()).ExecuteAsync(Command(proposal), TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Commits);
        Assert.Equal(ExtractionAcceptance.Rejected, store.Current.Acceptance);
    }
}
