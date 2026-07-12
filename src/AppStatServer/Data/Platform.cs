namespace AppStatServer.Data;

// Collapses the raw OS / user-agent strings we ingest — e.g. "Android (API level 34)",
// "Microsoft Windows 10.0.26200", or a full browser user-agent — into a small set of
// platform buckets, so the Events page filter and chart stay readable instead of listing
// every distinct OS build.
public static class Platform
{
    public const string Unknown = "Unknown";

    public static string Categorize(string? os)
    {
        if (string.IsNullOrWhiteSpace(os))
            return Unknown;

        // A browser user-agent (web build) embeds "Windows NT" / "Linux" substrings of its
        // own, so recognise the UA shape first and bucket it as Web before the OS keywords.
        // The Browser/WASM .NET head has no user-agent to report and instead sends its runtime
        // OS — RuntimeInformation.OSDescription is "Browser" there — so treat that, the
        // wasm/emscripten runtime tokens, and a literal "Web" as Web too. None of these appear
        // in a native OS string, so matching them here (before the OS keywords) is safe.
        if (os.Contains("Mozilla/", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("AppleWebKit", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("Emscripten", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("wasm", StringComparison.OrdinalIgnoreCase) ||
            os.Equals("Web", StringComparison.OrdinalIgnoreCase))
            return "Web";

        if (os.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "Android";
        if (os.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("iOS", StringComparison.OrdinalIgnoreCase))
            return "iOS";
        if (os.Contains("Mac", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
            return "macOS";
        if (os.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return "Windows";
        if (os.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            return "Linux";

        return "Other";
    }
}
