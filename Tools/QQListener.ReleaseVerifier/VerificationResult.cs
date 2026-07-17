namespace QQListener.ReleaseVerifier;

public sealed record VerificationResult(IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;

    public static VerificationResult Combine(params VerificationResult[] results) =>
        new(results.SelectMany(result => result.Errors).ToArray());
}
