namespace Aura.Application.Common.Exceptions;

public sealed class AppValidationException(string message) : Exception(message);
