namespace LifeTracker.State.Domain;

public readonly record struct Score
{
    public byte HalfUnits { get; }
    public decimal Value => HalfUnits / 2m;

    public Score(decimal value)
    {
        if (value is < 0 or > 10 || value * 2 != decimal.Truncate(value * 2))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Score must be 0–10 in steps of 0.5.");
        }

        HalfUnits = (byte)(value * 2);
    }

    public Score Invert() => new(10m - Value);
}
