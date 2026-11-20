namespace LifeTracker.Kernel;

public static class Identifiers
{
    public static Guid RequireNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier must not be empty.", parameterName);
        }

        return value;
    }
}
