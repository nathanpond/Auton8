namespace AutoNate.Web.Services.Content;

// Strict magic-byte sniffer for attachment uploads. The endpoint refuses
// uploads where the client-claimed MIME type doesn't match the bytes —
// e.g. HTML or SVG dressed up as image/png. Only formats with stable,
// unambiguous magic-byte signatures are recognised; everything else
// (including text/plain, text/csv, HTML, SVG, XML) is rejected.
public static class ContentTypeSniffer
{
    // For each sniff family, the set of client-claimed MIME types that
    // are considered consistent with those bytes. ZIP-family covers the
    // OOXML and ODF office formats; CFB-family covers legacy MS Office.
    private static readonly Dictionary<string, string[]> Compatible =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ["image/png"],
            ["image/jpeg"] = ["image/jpeg"],
            ["image/gif"] = ["image/gif"],
            ["image/webp"] = ["image/webp"],
            ["application/pdf"] = ["application/pdf"],
            ["application/zip"] =
            [
                "application/zip",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/vnd.oasis.opendocument.text",
                "application/vnd.oasis.opendocument.spreadsheet",
                "application/vnd.oasis.opendocument.presentation"
            ],
            ["application/x-ole-storage"] =
            [
                "application/msword",
                "application/vnd.ms-excel",
                "application/vnd.ms-powerpoint"
            ]
        };

    // Returns the sniff family canonical type, or null when the leading
    // bytes don't match any recognised signature.
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) &&
            bytes[5] == 0x61)
            return "image/gif";

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";

        if (bytes.Length >= 4 &&
            bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return "application/pdf";

        // ZIP local file header (0304), central dir (0506), or spanned (0708).
        if (bytes.Length >= 4 &&
            bytes[0] == 0x50 && bytes[1] == 0x4B &&
            (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07) &&
            (bytes[3] == 0x04 || bytes[3] == 0x06 || bytes[3] == 0x08))
            return "application/zip";

        // Compound File Binary Format (legacy DOC/XLS/PPT).
        if (bytes.Length >= 8 &&
            bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0 &&
            bytes[4] == 0xA1 && bytes[5] == 0xB1 && bytes[6] == 0x1A && bytes[7] == 0xE1)
            return "application/x-ole-storage";

        return null;
    }

    public static bool ClientTypeMatchesSniff(string sniffedCanonical, string? clientClaimed)
    {
        if (string.IsNullOrWhiteSpace(clientClaimed)) return false;
        if (!Compatible.TryGetValue(sniffedCanonical, out var allowed)) return false;
        foreach (var t in allowed)
        {
            if (string.Equals(t, clientClaimed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
