namespace QQListener.ReleaseVerifier;

public static class ReleaseVerifierCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string? root = null;
        var configuration = "Release";
        string? tag = null;
        var metadataOnly = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--configuration" when index + 1 < args.Length:
                    configuration = args[++index];
                    break;
                case "--tag" when index + 1 < args.Length:
                    tag = args[++index];
                    break;
                case "--metadata-only":
                    metadataOnly = true;
                    break;
                default:
                    error.WriteLine($"Unknown or incomplete argument '{args[index]}'.");
                    return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            error.WriteLine("--root is required.");
            return 1;
        }

        VerificationResult result;
        try
        {
            result = metadataOnly
                ? ReleaseMetadataVerifier.VerifyMetadata(root, configuration, tag)
                : ReleaseMetadataVerifier.VerifyProject(root, configuration, tag);
        }
        catch (Exception exception)
        {
            error.WriteLine($"Verifier failed: {exception.Message}");
            return 1;
        }

        if (!result.Success)
        {
            foreach (var item in result.Errors)
            {
                error.WriteLine(item);
            }

            return 1;
        }

        try
        {
            var version = ReleaseMetadataVerifier.ReadProjectVersion(
                Path.Combine(Path.GetFullPath(root), "Directory.Build.props"));
            output.WriteLine($"Release metadata verified for {version}.");
        }
        catch (Exception exception)
        {
            error.WriteLine($"Unable to print verified version: {exception.Message}");
            return 1;
        }

        return 0;
    }
}
