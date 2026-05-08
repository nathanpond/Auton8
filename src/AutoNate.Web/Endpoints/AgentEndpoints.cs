using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.Loop;
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

            http.Response.Headers["Content-Type"] = "text/event-stream";
            http.Response.Headers["Cache-Control"] = "no-cache, no-transform";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await foreach (var ev in session.SendMessageAsync(id, userId, request.Text!, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize<object>(ToWireEvent(ev));
                await http.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await http.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }).DisableAntiforgery();

        return app;
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
        AgentEvent.Error e => new { kind = e.Kind, message = e.Message },
        AgentEvent.Done d => new { kind = d.Kind },
        _ => new { kind = "unknown" }
    };
}

public sealed record class CreateConversationRequest(string? PageKey, Guid? ConnectionId);

public sealed record class RenameConversationRequest(string? Title);

public sealed record class SendMessageRequest(string? Text);
