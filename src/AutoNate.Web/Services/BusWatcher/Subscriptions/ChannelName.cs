namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Channel name format: kind[:scope][:id]
//
//   notification:user:{userId}
//   tasks:assigned-to:{userId}
//   record:{recordId}
//   firehose:all
//   topic:{topicName}
//
// The leftmost token (Kind) routes to the resolver / subscribe-gate handler.
// Everything to the right is parsed kind-specifically by the gate (e.g. the
// notification gate treats the second token as scope and the third as the
// userId). Parsing here only splits and validates the basic shape; semantic
// validation happens at subscribe time.
public sealed record ChannelName(string Full, string Kind, IReadOnlyList<string> Parts)
{
    public const char Separator = ':';

    public static bool TryParse(string? input, out ChannelName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var segments = trimmed.Split(Separator);
        if (segments.Length == 0 || string.IsNullOrEmpty(segments[0]))
        {
            return false;
        }

        // No empty segments — e.g. "notification::abc" is invalid.
        for (var i = 0; i < segments.Length; i++)
        {
            if (string.IsNullOrEmpty(segments[i]))
            {
                return false;
            }
        }

        var kind = segments[0];
        var parts = segments.Length == 1
            ? Array.Empty<string>()
            : segments[1..];

        result = new ChannelName(trimmed, kind, parts);
        return true;
    }
}
