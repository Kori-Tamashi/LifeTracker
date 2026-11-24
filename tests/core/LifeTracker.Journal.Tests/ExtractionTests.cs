using LifeTracker.Journal.Domain;
using Xunit;

namespace LifeTracker.Journal.Tests;

public sealed class ExtractionTests
{
    internal static readonly Guid Profile = Guid.NewGuid();
    internal static readonly Guid Conversation = Guid.NewGuid();
    internal static readonly Guid Message = Guid.NewGuid();
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    internal static ExtractionCandidate Candidate(ExtractionKind kind = ExtractionKind.Event,
        Guid? profile = null, Guid? conversation = null, ClaimSubject subject = ClaimSubject.Owner,
        ClaimPolarity polarity = ClaimPolarity.Negated) => new(Guid.NewGuid(), kind, "Synthetic report",
            subject, polarity, new(profile ?? Profile, conversation ?? Conversation, Message, 1, "Synthetic owner message", 0, 15));
    internal static ExtractionRevision Proposal(bool auto = false, params ExtractionCandidate[] candidates) =>
        ExtractionRevision.Propose(Profile, Guid.NewGuid(), Conversation, Guid.NewGuid(), "synthetic-model-v1",
            candidates.Length == 0 ? [Candidate()] : candidates, Now, auto);

    [Fact]
    public void PendingAndRejectedNeverEnterMemoryAndReviewPreservesOriginal()
    {
        var original = Proposal();
        Assert.False(original.EligibleForMemory);
        var rejected = original.Decide(1, ExtractionDecision.Reject, Now.AddMinutes(1));
        Assert.False(rejected.EligibleForMemory);
        Assert.Equal(ExtractionAcceptance.Pending, original.Acceptance);
        Assert.Equal(1, original.Revision);
        Assert.Equal(2, rejected.Revision);
        Assert.Throws<InvalidOperationException>(() => rejected.Decide(2, ExtractionDecision.Confirm, Now));
    }

    [Theory]
    [InlineData(ExtractionKind.Event)]
    [InlineData(ExtractionKind.PersonMention)]
    [InlineData(ExtractionKind.Hypothesis)]
    public void AutoConfirmationPreservesKnowledgeTypeSubjectAndNegation(ExtractionKind kind)
    {
        var candidate = Candidate(kind, subject: ClaimSubject.OtherPerson);
        var accepted = Proposal(true, candidate);
        Assert.True(accepted.EligibleForMemory);
        Assert.Equal(ExtractionReviewKind.AutoConfirmed, accepted.ReviewKind);
        Assert.Equal(kind, accepted.Candidates[0].Kind);
        Assert.Equal(ClaimSubject.OtherPerson, accepted.Candidates[0].Subject);
        Assert.Equal(ClaimPolarity.Negated, accepted.Candidates[0].Polarity);
    }

    [Fact]
    public void ConfirmAndEditDoNotMutatePendingRevisionOrCallerCollection()
    {
        var original = Proposal();
        var accepted = original.Decide(1, ExtractionDecision.Confirm, Now);
        Assert.True(accepted.EligibleForMemory);
        var replacement = Candidate(ExtractionKind.Hypothesis);
        var edited = new[] { replacement };
        var result = original.Decide(1, ExtractionDecision.Edit, Now, edited);
        edited[0] = Candidate();
        Assert.Same(replacement, result.Candidates[0]);
        Assert.NotEqual(replacement.Id, original.Candidates[0].Id);
        Assert.Equal(ExtractionReviewKind.Edited, result.ReviewKind);
        Assert.Throws<InvalidOperationException>(() => accepted.Decide(2, ExtractionDecision.Reject, Now));
    }

    [Fact]
    public void OldRevisionOrStaleEvidenceCannotBeConfirmed()
    {
        var original = Proposal();
        Assert.Throws<InvalidOperationException>(() => original.Decide(2, ExtractionDecision.Confirm, Now));
        var stale = original.SourceRevised(Message, 2, Now);
        Assert.True(stale.IsStale);
        Assert.False(stale.EligibleForMemory);
        Assert.Throws<InvalidOperationException>(() => stale.Decide(2, ExtractionDecision.Confirm, Now));
        var accepted = Proposal(true);
        Assert.False(accepted.SourceRevised(Message, 2, Now).EligibleForMemory);
        Assert.Same(accepted, accepted.SourceRevised(Message, 1, Now));
        Assert.Same(accepted, accepted.SourceRevised(Guid.NewGuid(), 2, Now));
        Assert.Same(stale, stale.SourceRevised(Message, 3, Now));
    }

    [Fact]
    public void ProposalRejectsCrossProfileCrossConversationDuplicateAndEmptyCandidates()
    {
        var duplicate = Candidate();
        Assert.Throws<ArgumentException>(() => Proposal(false, duplicate, duplicate));
        Assert.Throws<ArgumentException>(() => Proposal(false, Candidate(profile: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Proposal(false, Candidate(conversation: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => ExtractionRevision.Propose(Profile, Guid.NewGuid(), Conversation,
            Guid.NewGuid(), "synthetic", [], Now));
    }

    [Fact]
    public void DecisionPayloadMustMatchDecisionAndCannotEraseAllCandidates()
    {
        var original = Proposal();
        Assert.Throws<ArgumentException>(() => original.Decide(1, ExtractionDecision.Confirm, Now, [Candidate()]));
        Assert.Throws<ArgumentException>(() => original.Decide(1, ExtractionDecision.Reject, Now, [Candidate()]));
        Assert.Throws<ArgumentException>(() => original.Decide(1, ExtractionDecision.Edit, Now));
        Assert.Throws<ArgumentException>(() => original.Decide(1, ExtractionDecision.Edit, Now, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.Decide(1, (ExtractionDecision)99, Now));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 99)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    public void EvidenceRejectsInvalidRangesAndSplitSurrogates(int start, int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => new EvidenceSpan(Profile, Conversation, Message, 1, "😀x", start, length));
    }

    [Fact]
    public void EvidenceUsesActualSourceSubstringAndKeepsMessageRevision()
    {
        var span = new EvidenceSpan(Profile, Conversation, Message, 3, "😀Synthetic quote", 2, 9);
        Assert.Equal("Synthetic", span.Quote);
        Assert.Equal(3, span.MessageRevision);
        Assert.Equal(2, span.Start);
        Assert.Throws<ArgumentException>(() => new ExtractionCandidate(Guid.NewGuid(), ExtractionKind.Event,
            " ", ClaimSubject.Owner, ClaimPolarity.Affirmed, span));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionCandidate(Guid.NewGuid(), (ExtractionKind)99,
            "synthetic", ClaimSubject.Owner, ClaimPolarity.Affirmed, span));
    }

    [Fact]
    public void EqualPersonNamesNeverMergeDistinctMentions()
    {
        var first = Candidate(ExtractionKind.PersonMention);
        var second = Candidate(ExtractionKind.PersonMention);
        var accepted = Proposal(true, first, second);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(2, accepted.Candidates.Count);
        Assert.NotEqual(accepted.Candidates[0].Id, accepted.Candidates[1].Id);
    }
}
