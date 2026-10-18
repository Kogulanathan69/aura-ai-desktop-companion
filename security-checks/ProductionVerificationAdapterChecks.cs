using Aura.Api.Security;
using Aura.Application.Verifications;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionVerificationAdapterChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        var review = File.ReadAllText(Path.Combine(root,
            "STEP-16G-INDEPENDENT-VERIFICATION-PRODUCTION-ADAPTER.md"));
        check(typeof(IExecutionEvidenceSource).IsAssignableFrom(
                typeof(UnavailableExecutionEvidenceSource)) &&
            review.Contains("ProofSource=None") && review.Contains("ProofVersion=0") &&
            !api.Contains("AddScoped<IExecutionEvidenceSource") &&
            inspector.Contains("ReviewedVerification = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Verification,
            "17G unavailable source and missing independent proof cannot assert verification");
        return Task.CompletedTask;
    }
}
