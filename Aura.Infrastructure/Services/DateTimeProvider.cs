using Aura.Application.Common.Interfaces;

namespace Aura.Infrastructure.Services;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}