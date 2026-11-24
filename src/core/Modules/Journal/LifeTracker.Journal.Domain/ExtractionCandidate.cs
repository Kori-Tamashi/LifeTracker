using LifeTracker.Kernel;

namespace LifeTracker.Journal.Domain;

public enum ExtractionKind { Event, PersonMention, Hypothesis }
public enum ClaimSubject { Owner, OtherPerson }
public enum ClaimPolarity { Affirmed, Negated, Uncertain }

/// <summary>A traceable owner-message excerpt. Offsets use .NET UTF-16 units, never model-invented quotes.</summary>
public sealed record EvidenceSpan
{
    public Guid ProfileId { get; }
    public Guid ConversationId { get; }
    public Guid MessageId { get; }
    public int MessageRevision { get; }
    public int Start { get; }
    public int Length { get; }
    public string Quote { get; }

    public EvidenceSpan(Guid profileId, Guid conversationId, Guid messageId, int messageRevision,
        string ownerMessageText, int start, int length)
    {
        ProfileId = Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        ConversationId = Identifiers.RequireNonEmpty(conversationId, nameof(conversationId));
        MessageId = Identifiers.RequireNonEmpty(messageId, nameof(messageId));
        ArgumentNullException.ThrowIfNull(ownerMessageText);
        if (messageRevision < 1 || start < 0 || length < 1 || start > ownerMessageText.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        var end = start + length;
        if ((start > 0 && char.IsHighSurrogate(ownerMessageText[start - 1]) && char.IsLowSurrogate(ownerMessageText[start]))
            || (end < ownerMessageText.Length && char.IsHighSurrogate(ownerMessageText[end - 1]) && char.IsLowSurrogate(ownerMessageText[end])))
        {
            throw new ArgumentException("Evidence cannot split a Unicode surrogate pair.", nameof(length));
        }
        Quote = ownerMessageText.Substring(start, length);
        ArgumentException.ThrowIfNullOrWhiteSpace(Quote);
        MessageRevision = messageRevision;
        Start = start;
        Length = length;
    }
}

public sealed record ExtractionCandidate
{
    public Guid Id { get; }
    public ExtractionKind Kind { get; }
    public string Content { get; }
    public ClaimSubject Subject { get; }
    public ClaimPolarity Polarity { get; }
    public EvidenceSpan Evidence { get; }

    public ExtractionCandidate(Guid id, ExtractionKind kind, string content, ClaimSubject subject,
        ClaimPolarity polarity, EvidenceSpan evidence)
    {
        Id = Identifiers.RequireNonEmpty(id, nameof(id));
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(subject) || !Enum.IsDefined(polarity))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(evidence);
        Kind = kind;
        Content = content;
        Subject = subject;
        Polarity = polarity;
        Evidence = evidence;
    }
}
