using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.PageQuery;
using Microsoft.AspNetCore.Http.Features;

namespace AutoNate.Web.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent").RequireAuthorization();

        group.MapGet("/conversations", async (
            string? pageKey,
            HttpContext http,
            IAgentConversationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var rows = await store.ListForUserAsync(userId, pageKey, take: 50, ct);
            return Results.Ok(rows);
        });

        group.MapPost("/conversations", async (
            CreateConversationRequest request,
            HttpContext http,
            IAgentSession session,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var convo = await session.StartAsync(userId, request.PageKey ?? "default", request.ConnectionId, ct);
            return Results.Created($"/api/agent/conversations/{convo.Id}", convo);
        }).DisableAntiforgery();

        group.MapGet("/conversations/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAgentConversationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var detail = await store.GetForUserAsync(id, userId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPatch("/conversations/{id:guid}", async (
            Guid id,
            RenameConversationRequest request,
            HttpContext http,
            IAgentConversationStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var updated = await store.RenameAsync(id, userId, request.Title ?? string.Empty, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).DisableAntiforgery();

        group.MapDelete("/conversations/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAgentConversationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var deleted = await store.DeleteAsync(id, userId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        // SSE endpoint. Returns text/event-stream of `data: {...}` frames, one
        // per AgentEvent. Uses HttpContext.RequestAborted as the upstream
        // cancellation token, so a closed browser tab tears down the loop and
        // any pending tool calls.
        group.MapPost("/conversations/{id:guid}/messages", async (
            Guid id,
            SendMessageRequest request,
            HttpContext http,
            IAgentSession session,
            IAgentConversationStore store,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Text))
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var userId = GetUserId(http);
            if (userId == Guid.Empty)
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Validate the optional page snapshot before opening the SSE stream
            // so a 400 is a real 400 (not an SSE frame).
            PageContextInput? pageContext = null;
            if (request.PageContext is { } pc)
            {
                if (string.IsNullOrWhiteSpace(pc.PageKey))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await http.Response.WriteAsync("pageContext.pageKey is required.", ct);
                    return;
                }
                var conv = await store.GetForUserAsync(id, userId, ct);
                if (conv is null)
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (!string.Equals(pc.PageKey, conv.Conversation.PageKey, StringComparison.Ordinal))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await http.Response.WriteAsync(
                        $"pageContext.pageKey '{pc.PageKey}' does not match conversation pageKey '{conv.Conversation.PageKey}'.",
                        ct);
                    return;
                }
                // Size cap on Data. GetRawText returns the underlying JSON
                // text length — cheap.
                int rawLen;
                try { rawLen = pc.Data.GetRawText().Length; }
                catch (InvalidOperationException)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await http.Response.WriteAsync("pageContext.data is invalid.", ct);
                    return;
                }
                if (rawLen > AgentSession.MaxSnapshotDataBytes)
                {
                    http.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
                    await http.Response.WriteAsync(
                        $"pageContext.data exceeds {AgentSession.MaxSnapshotDataBytes} bytes (got {rawLen}).",
                        ct);
                    return;
                }
                pageContext = new PageContextInput(
                    PageKey: pc.PageKey,
                    SchemaVersion: pc.SchemaVersion,
                    Summary: pc.Summary,
                    Version: pc.Version,
                    Data: pc.Data);
            }

            http.Response.Headers["Content-Type"] = "text/event-stream";
            http.Response.Headers["Cache-Control"] = "no-cache, no-transform";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await foreach (var ev in session.SendMessageAsync(id, userId, request.Text!, pageContext, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize<object>(ToWireEvent(ev));
                await http.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await http.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }).DisableAntiforgery();

        // Receives the SPA's reply to a server-issued PageQueryRequested event.
        // Resolves the awaiting TaskCompletionSource inside the singleton
        // router so the calling tool unblocks and can return its result to the
        // model. Auth: only the conversation owner can resolve their own
        // queries (defense in depth — the queryId is itself a secret).
        group.MapPost("/conversations/{id:guid}/page-query-results", async (
            Guid id,
            PageQueryResultRequest request,
            HttpContext http,
            IAgentConversationStore store,
            IPageQueryRouter router,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.QueryId) || request.Result is null)
            {
                return Results.BadRequest("queryId and result are required.");
            }
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var conv = await store.GetForUserAsync(id, userId, ct);
            if (conv is null) return Results.NotFound();

            PageQueryResult result = request.Result.Ok
                ? new PageQueryResult.Success(request.Result.Data ?? EmptyJsonObject())
                : new PageQueryResult.Failure(
                    string.IsNullOrWhiteSpace(request.Result.Error) ? "spa_error" : request.Result.Error!,
                    request.Result.Message);

            var resolved = router.TryResolve(id, request.QueryId, result);
            return resolved ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        return app;
    }

    private static JsonElement EmptyJsonObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static Guid GetUserId(HttpContext context)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    private static object ToWireEvent(AgentEvent ev) => ev switch
    {
        AgentEvent.MessageStarted m => new { kind = m.Kind, messageId = m.MessageId },
        AgentEvent.TextDelta t => new { kind = t.Kind, delta = t.Delta },
        AgentEvent.ToolStarted ts => new { kind = ts.Kind, toolCallId = ts.ToolCallId, toolUseId = ts.ToolUseId, name = ts.Name, args = ts.Args },
        AgentEvent.ToolCompleted tc => new { kind = tc.Kind, toolCallId = tc.ToolCallId, toolUseId = tc.ToolUseId, result = tc.Result, durationMs = tc.DurationMs },
        AgentEvent.ToolFailed tf => new { kind = tf.Kind, toolCallId = tf.ToolCallId, toolUseId = tf.ToolUseId, error = tf.ErrorMessage, durationMs = tf.DurationMs },
        AgentEvent.MessageCompleted mc => new
        {
            kind = mc.Kind,
            messageId = mc.MessageId,
            stopReason = mc.StopReason.ToString().ToLowerInvariant(),
            usage = mc.Usage is null ? null : new
            {
                inputTokens = mc.Usage.InputTokens,
                outputTokens = mc.Usage.OutputTokens,
                cacheReadTokens = mc.Usage.CacheReadTokens,
                cacheWriteTokens = mc.Usage.CacheWriteTokens
            }
        },
        AgentEvent.PageQueryRequested pq => new { kind = pq.Kind, queryId = pq.QueryId, topic = pq.Topic, args = pq.Args },
        AgentEvent.Error e => new { kind = e.Kind, message = e.Message },
        AgentEvent.Done d => new { kind = d.Kind },
        _ => new { kind = "unknown" }
    };
}

public sealed record class CreateConversationRequest(string? PageKey, Guid? ConnectionId);

public sealed record class RenameConversationRequest(string? Title);

public sealed record class SendMessageRequest(string? Text, PageContextDto? PageContext = null);

// Wire-format mirror of the SPA's PageSnapshot. Pure DTO; AgentEndpoints
// validates and converts to PageContextInput before handing off to the
// session.
public sealed record class PageContextDto(
    string PageKey,
    int SchemaVersion,
    string? Summary,
    long Version,
    JsonElement Data);

// The SPA's reply to a PageQueryRequested SSE event.
public sealed record class PageQueryResultRequest(string? QueryId, PageQueryResultDto? Result);

public sealed record class PageQueryResultDto(bool Ok, JsonElement? Data, string? Error, string? Message);
