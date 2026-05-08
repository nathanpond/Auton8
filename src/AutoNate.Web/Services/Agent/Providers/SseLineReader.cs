using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace AutoNate.Web.Services.Agent.Providers;

// Tiny SSE frame reader shared by AnthropicChatProvider and OpenAIChatProvider.
// Both providers stream as a sequence of "event: NAME\n" + "data: JSON\n\n"
// blocks; a few wrinkles (Anthropic puts an event before the data line; OpenAI
// uses only data lines and a trailing "data: [DONE]") are easier to handle in
// the per-provider code on top of this primitive than to abstract here.
public static class SseLineReader
{
    public static async IAsyncEnumerable<SseFrame> ReadFramesAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = PipeReader.Create(stream);
        string? eventName = null;
        var dataBuffer = new StringBuilder();

        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            while (TryReadLine(ref buffer, out var line))
            {
                if (line.Length == 0)
                {
                    // Blank line terminates a frame.
                    if (dataBuffer.Length > 0)
                    {
                        yield return new SseFrame(eventName, dataBuffer.ToString());
                        dataBuffer.Clear();
                    }
                    eventName = null;
                    continue;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                    dataBuffer.Append(line["data:".Length..].TrimStart());
                }
                // Other field types (id:, retry:) are ignored — neither
                // provider uses them in their streaming protocols today.
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                // Flush any trailing frame.
                if (dataBuffer.Length > 0)
                {
                    yield return new SseFrame(eventName, dataBuffer.ToString());
                }
                break;
            }
        }

        await reader.CompleteAsync().ConfigureAwait(false);
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out string line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = string.Empty;
            return false;
        }

        var slice = buffer.Slice(0, position.Value);
        var raw = Encoding.UTF8.GetString(slice);
        // Tolerate CRLF.
        line = raw.EndsWith('\r') ? raw[..^1] : raw;
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }
}

public sealed record class SseFrame(string? Event, string Data);
