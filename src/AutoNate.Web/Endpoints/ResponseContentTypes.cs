namespace AutoNate.Web.Endpoints;

/// <summary>
/// Downgrades a stored, uploader-supplied Content-Type to
/// <c>application/octet-stream</c> when replaying it would let the browser
/// execute the bytes in our own origin.
/// </summary>
/// <remarks>
/// The value on a stored file is whatever the uploader put in the multipart
/// part header — it is not evidence of what the bytes are. Echoing
/// <c>text/html</c> or <c>image/svg+xml</c> back on a same-origin download is
/// one dropped <c>fileDownloadName</c> (or one added inline preview) away from
/// stored XSS against the session cookie.
///
/// Page attachments have always done this; the datastore file download did not,
/// which is archived-65. Shared here so the two cannot drift — a new download route
/// should reach for this rather than reinvent the list.
/// </remarks>
public static class ResponseContentTypes
{
    public const string Fallback = "application/octet-stream";

    private static readonly HashSet<string> Dangerous =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "text/html",
            "application/xhtml+xml",
            "image/svg+xml",
            "application/javascript",
            "text/javascript",
            "application/ecmascript",
            "text/ecmascript",
            "application/xml",
            "text/xml"
        };

    public static string Sanitize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return Fallback;
        var trimmed = contentType.Trim();
        // A parameterised type ("text/html; charset=utf-8") must not slip past
        // the set by virtue of its suffix.
        var separator = trimmed.IndexOf(';');
        var mediaType = separator >= 0 ? trimmed[..separator].Trim() : trimmed;
        return Dangerous.Contains(mediaType) ? Fallback : trimmed;
    }
}
