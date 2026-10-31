namespace LabMonitoring.Application.Common.Exceptions;

/// <summary>Requested entity does not exist → HTTP 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>Caller is outside the assigned room scope (SRS FR-8.2) → HTTP 403.</summary>
public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : Exception(message);

/// <summary>Request conflicts with current state (e.g. duplicate code, already approved) → HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
