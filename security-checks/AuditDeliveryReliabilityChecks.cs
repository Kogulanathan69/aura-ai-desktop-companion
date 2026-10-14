internal static class AuditDeliveryReliabilityChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();

        var sink = File.ReadAllText(Path.Combine(
            root,
            "Aura.Infrastructure",
            "Services",
            "SecurityAuditPersistenceSink.cs"));

        var apiProgram = File.ReadAllText(Path.Combine(
            root,
            "Aura.Api",
            "Program.cs"));

        check(
            sink.Contains(
                "factory.CreateDbContextAsync",
                StringComparison.Ordinal),
            "13F audit persistence still uses an independent database context");

        check(
            sink.Contains(
                "db.SecurityAuditEvents.Add",
                StringComparison.Ordinal) &&
            sink.Contains(
                "SaveChangesAsync(cancellationToken)",
                StringComparison.Ordinal),
            "13F audit sink performs one direct bounded insert");

        check(
            !sink.Contains(
                "BeginTransactionAsync",
                StringComparison.Ordinal),
            "13F audit sink does not falsely claim a shared transaction");

        check(
            !sink.Contains(
                "ActionExecutionStates.Add",
                StringComparison.Ordinal) &&
            !sink.Contains(
                "ActionExecutionStates.Update",
                StringComparison.Ordinal),
            "13F audit sink does not mutate execution state");

        check(
            !sink.Contains(
                "Retry",
                StringComparison.OrdinalIgnoreCase),
            "13F audit sink has no hidden retry behavior");

        check(
            !sink.Contains(
                "Outbox",
                StringComparison.OrdinalIgnoreCase),
            "13F transactional outbox delivery has not been fabricated");

        check(
            !apiProgram.Contains(
                "SecurityAuditPersistenceSink",
                StringComparison.Ordinal),
            "13F direct audit persistence sink remains unregistered in production");

        check(
            !apiProgram.Contains(
                "MigrateAsync(",
                StringComparison.Ordinal) &&
            !apiProgram.Contains(
                "Migrate()",
                StringComparison.Ordinal),
            "13F application startup still does not auto-apply migrations");

        return Task.CompletedTask;
    }
}