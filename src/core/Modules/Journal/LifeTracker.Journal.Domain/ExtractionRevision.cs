using LifeTracker.Kernel;

namespace LifeTracker.Journal.Domain;

public enum ExtractionAcceptance { Pending, Accepted, Rejected }
public enum ExtractionDecision { Confirm, Edit, Reject }
public enum ExtractionReviewKind { Proposed, AutoConfirmed, Confirmed, Edited, Rejected, SourceRevised }

/// <summary>Immutable extraction revision. Acceptance never promotes a hypothesis to a fact.</summary>
public sealed class ExtractionRevision
{
    public Guid ProfileId { get; }
    public Guid Id { get; }
    public Guid ConversationId { get; }
    public Guid SourceEventId { get; }
    public string ModelManifestId { get; }
    public int Revision { get; }
    public DateTimeOffset RecordedAt { get; }
    public ExtractionAcceptance Acceptance { get; }
    public ExtractionReviewKind ReviewKind { get; }
    public bool IsStale { get; }
    public IReadOnlyList<ExtractionCandidate> Candidates { get; }
    public bool EligibleForMemory => Acceptance == ExtractionAcceptance.Accepted && !IsStale;

    private ExtractionRevision(Guid profileId, Guid id, Guid conversationId, Guid sourceEventId, string modelManifestId,
        int revision, DateTimeOffset at, ExtractionAcceptance acceptance, ExtractionReviewKind reviewKind,
        bool stale, IEnumerable<ExtractionCandidate> candidates)
    {
        ProfileId = Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        Id = Identifiers.RequireNonEmpty(id, nameof(id));
        ConversationId = Identifiers.RequireNonEmpty(conversationId, nameof(conversationId));
        SourceEventId = Identifiers.RequireNonEmpty(sourceEventId, nameof(sourceEventId));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelManifestId);
        ArgumentNullException.ThrowIfNull(candidates);
        var copy = candidates.ToArray();
        if (copy.Length == 0 || copy.Any(c => c is null || c.Evidence.ProfileId != profileId || c.Evidence.ConversationId != conversationId)
            || copy.Select(c => c.Id).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Candidates require distinct IDs and evidence within the conversation/profile.", nameof(candidates));
        }
        ModelManifestId = modelManifestId;
        Revision = revision;
        RecordedAt = at.ToUniversalTime();
        Acceptance = acceptance;
        ReviewKind = reviewKind;
        IsStale = stale;
        Candidates = Array.AsReadOnly(copy);
    }

    public static ExtractionRevision Propose(Guid profileId, Guid id, Guid conversationId, Guid sourceEventId,
        string modelManifestId, IEnumerable<ExtractionCandidate> candidates, DateTimeOffset at, bool autoConfirm = false) =>
        new(profileId, id, conversationId, sourceEventId, modelManifestId, 1, at,
            autoConfirm ? ExtractionAcceptance.Accepted : ExtractionAcceptance.Pending,
            autoConfirm ? ExtractionReviewKind.AutoConfirmed : ExtractionReviewKind.Proposed, false, candidates);

    public ExtractionRevision Decide(int expectedRevision, ExtractionDecision decision, DateTimeOffset at,
        IReadOnlyList<ExtractionCandidate>? editedCandidates = null)
    {
        if (expectedRevision != Revision || Acceptance != ExtractionAcceptance.Pending || IsStale)
        {
            throw new InvalidOperationException("Extraction revision is no longer available for review.");
        }
        if (!Enum.IsDefined(decision)) { throw new ArgumentOutOfRangeException(nameof(decision)); }
        if ((decision == ExtractionDecision.Edit) != (editedCandidates is not null))
        {
            throw new ArgumentException("Only edit supplies replacement candidates.", nameof(editedCandidates));
        }
        var kind = decision switch
        {
            ExtractionDecision.Confirm => ExtractionReviewKind.Confirmed,
            ExtractionDecision.Edit => ExtractionReviewKind.Edited,
            _ => ExtractionReviewKind.Rejected
        };
        return new(ProfileId, Id, ConversationId, SourceEventId, ModelManifestId, checked(Revision + 1), at,
            decision == ExtractionDecision.Reject ? ExtractionAcceptance.Rejected : ExtractionAcceptance.Accepted,
            kind, false, editedCandidates ?? Candidates);
    }

    public ExtractionRevision SourceRevised(Guid messageId, int newMessageRevision, DateTimeOffset at)
    {
        Identifiers.RequireNonEmpty(messageId, nameof(messageId));
        if (newMessageRevision < 1) { throw new ArgumentOutOfRangeException(nameof(newMessageRevision)); }
        if (IsStale || !Candidates.Any(c => c.Evidence.MessageId == messageId && newMessageRevision > c.Evidence.MessageRevision))
        {
            return this;
        }
        return new(ProfileId, Id, ConversationId, SourceEventId, ModelManifestId, checked(Revision + 1), at,
            Acceptance, ExtractionReviewKind.SourceRevised, true, Candidates);
    }
}
