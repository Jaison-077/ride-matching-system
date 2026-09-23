namespace RideMatching.Api.Domain;

/// <summary>Base type for expected domain errors that map to specific HTTP responses.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

/// <summary>A requested driver does not exist.</summary>
public sealed class DriverNotFoundException : DomainException
{
    public DriverNotFoundException(Guid driverId)
        : base($"The requested driver '{driverId}' does not exist.") { }
}

/// <summary>A requested ride does not exist.</summary>
public sealed class RideNotFoundException : DomainException
{
    public RideNotFoundException(Guid rideId)
        : base($"The requested ride '{rideId}' does not exist.") { }
}

/// <summary>Input validation failed for a request.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message) : base(message) { }
}
