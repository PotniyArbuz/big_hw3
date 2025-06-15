using System;
namespace Contracts;

public record PaymentRequested(Guid OrderId, Guid UserId, decimal Amount);
public record PaymentProcessed(Guid OrderId, Guid UserId, bool Success, string? Reason = null);
