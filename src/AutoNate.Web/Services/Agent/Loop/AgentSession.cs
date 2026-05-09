using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.PageQuery;
using AutoNate.Web.Services.Agent.Providers;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Agent.Loop;

public sealed class AgentSession : IAgentSession
{
    // Tool names that the chatbot.internetAccessEnabled setting controls.
    // Both fetch_url and web_search reach the public internet; both ride
    // on the same toggle so admins have one switch.
    private static readonly HashSet<string> InternetGatedTools = new(StringComparer.Ordinal)
    {
        WebFetchSkill.ToolName,
        WebSearchSkill.ToolName
    };

    // Caps for the SPA-provided page snapshot. Validated again in AgentSession
    // (defense in depth) so tests / direct callers that bypass the endpoint
    // can't smuggle oversized payloads into the system prompt.
    public const int MaxSnapshotDataBytes = 64 * 1024;
    public const int MaxSnapshotSummaryChars = 1024;

    private readonly IAgentConversationStore _conversationStore;
    private readonly IChatProviderResolver _providerResolver;
    private readonly ISkillRegistry _skillRegistry;
    private readonly SystemPromptBuilder _promptBuilder;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly ISiteSettingsStore _siteSettingsStore;
    private readonly PageQueryChannel _pageQueryChannel;
    private readonly PageActionChannel _pageActionChannel;
    private readonly IServiceProvider _services;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentSession> _logger;

    public AgentSession(
        IAgentConversationStore conversationStore,
        IChatProviderResolver providerResolver,
        ISkillRegistry skillRegistry,
        SystemPromptBuilder promptBuilder,
        IAuditEventPublisher auditPublisher,
        ISiteSettingsStore siteSettingsStore,
        PageQueryChannel pageQueryChannel,
        PageActionChannel pageActionChannel,
        IServiceProvider services,
        IOptions<AgentOptions> options,
        ILogger<AgentSession> logger)
    {
        _conversationStore = conversationStore;
        _providerResolver = providerResolver;
        _skillRegistry = skillRegistry;
        _promptBuilder = promptBuilder;
        _auditPublisher = auditPublisher;
        _siteSettingsStore = siteSettingsStore;
        _pageQueryChannel = pageQueryChannel;
        _pageActionChannel = pageActionChannel;
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AgentConversationDto> StartAsync(
        Guid userId,
        string pageKey,
        Guid? connectionId,
        CancellationToken cancellationToken = default)
    {
        // We don't probe the provider here — just remember the connection
        // choice. SendMessageAsync resolves the provider on demand so a
        // misconfigured connection only fails at send time.
        return await _conversationStore.CreateAsync(
            userId,
            pageKey,
            connectionId,
            providerKind: null,
            modelId: null,
            cancellationToken);
    }

    public async IAsyncEnumerable<AgentEvent> SendMessageAsync(
        Guid conversationId,
        Guid userId,
        string userText,
        PageContextInput? pageContext = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var detail = await _conversationStore.GetForUserAsync(conversationId, userId, cancellationToken);
        if (detail is null)
        {
            yield return new AgentEvent.Error("Conversation not found.");
            yield return new AgentEvent.Done();
            yield break;
        }
        var conversation = detail.Conversation;

        // Validate / normalize the SPA-supplied page snapshot. Defense in
        // depth: the endpoint already enforces these, but tests and direct
        // callers may skip it. A bad snapshot is dropped (never throws) so
        // the conversation can still proceed without page awareness.
        PageContextSnapshot? snapshot = null;
        if (pageContext is not null)
        {
            snapshot = TryNormalizeSnapshot(pageContext, conversation.PageKey);
        }

        // Persist the user message first.
        var userBlocks = new ChatContentBlock[] { new ChatContentBlock.TextBlock(userText) };
        var userMessageId = await _conversationStore.AppendMessageAsync(
            conversationId,
            ChatRole.User,
            userBlocks,
            providerKind: null,
            modelId: null,
            usage: null,
            stopReason: null,
            cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.MessageUserSent,
            AgentResourceKinds.Message,
            resource: new { conversationId, messageId = userMessageId },
            details: new
            {
                length = userText.Length,
                pageKey = conversation.PageKey,
                pageSummary = snapshot?.Summary,
                pageSchemaVersion = snapshot?.SchemaVersion,
                pageVersion = snapshot?.Version
            },
            cancellationToken);

        // Resolve the provider. If the conversation has no connection, fall
        // back to the user's default (any kind that has a default).
        IChatProvider? provider = null;
        if (conversation.ConnectionId is Guid cid)
        {
            provider = await _providerResolver.ResolveAsync(cid, cancellationToken);
        }
        provider ??= await _providerResolver.ResolveDefaultForKindAsync("LlmProvider:Anthropic", cancellationToken)
            ?? await _providerResolver.ResolveDefaultForKindAsync("LlmProvider:OpenAI", cancellationToken);

        if (provider is null)
        {
            yield return new AgentEvent.Error("No LLM connection configured. Add one in Admin → External Connections.");
            yield return new AgentEvent.Done();
            yield break;
        }

        // Build a principal carrying the NameIdentifier claim so skills that
        // call IAuthorizer (e.g. ManageRecordTypesSkill) can resolve the actor.
        // The endpoint's HttpContext.User isn't piped through here — Authorizer
        // only needs the user id to load grants/roles from the DB itself.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, authenticationType: "AgentSession"));

        var sessionContext = new AgentSessionContext(
            User: principal,
            UserId: userId,
            PageKey: conversation.PageKey,
            ConversationId: conversationId,
            PageContext: snapshot);

        var systemPrompt = _promptBuilder.Build(
            sessionContext,
            _skillRegistry.All,
            userDisplayName: null,
            userRoles: Array.Empty<string>());

        var history = (await _conversationStore.LoadMessagesAsync(conversationId, cancellationToken)).ToList();
        var maxTokens = _options.DefaultMaxTokens;

        // Apply per-turn capability gates from site settings. Read once per
        // SendMessageAsync call so toggles take effect on the next user
        // message, not mid-turn.
        var internetAccessEnabled = await _siteSettingsStore.GetBoolAsync(
            SiteSettingsKeys.ChatbotInternetAccessEnabled, cancellationToken);
        var allTools = _skillRegistry.ChatTools;
        IReadOnlyList<ChatTool> filteredTools = internetAccessEnabled
            ? allTools
            : allTools.Where(t => !InternetGatedTools.Contains(t.Name)).ToList();

        var iteration = 0;
        while (iteration < _options.MaxIterations)
        {
            iteration++;
            var request = new ChatRequest(
                Messages: history,
                SystemPrompt: systemPrompt,
                Tools: filteredTools,
                ModelId: conversation.ModelId ?? "default",
                MaxTokens: maxTokens);

            var assistantBlocks = new List<ChatContentBlock>();
            var pendingTextBuffer = new System.Text.StringBuilder();
            // Per-tool_use_id, the buffered ToolUseStarted info we emit when
            // the tool completes.
            var toolStarts = new Dictionary<string, (string Name, JsonElement Args)>();
            var assistantMessageId = Guid.Empty;
            ChatStopReason? stopReason = null;
            Usage? usage = null;
            string? errorText = null;

            await foreach (var chunk in provider.StreamAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (assistantMessageId == Guid.Empty)
                {
                    // Materialise a Guid for the assistant message early so the
                    // client can correlate streamed deltas to the eventual row.
                    assistantMessageId = Guid.NewGuid();
                    await _auditPublisher.PublishAsync(
                        AgentEventTopic.TopicName,
                        AgentEventTypes.MessageAssistantStarted,
                        AgentResourceKinds.Message,
                        resource: new { conversationId, messageId = assistantMessageId, iteration },
                        details: new { providerKind = provider.Kind, modelId = conversation.ModelId },
                        cancellationToken);
                    yield return new AgentEvent.MessageStarted(assistantMessageId);
                }

                switch (chunk)
                {
                    case ChatStreamChunk.TextDelta td:
                        pendingTextBuffer.Append(td.Delta);
                        yield return new AgentEvent.TextDelta(td.Delta);
                        break;
                    case ChatStreamChunk.ToolUseStarted ts:
                        toolStarts[ts.ToolUseId] = (ts.Name, EmptyObject());
                        break;
                    case ChatStreamChunk.ToolUseCompleted tc:
                        toolStarts[tc.ToolUseId] = (tc.Name, tc.Args);
                        break;
                    case ChatStreamChunk.MessageStop ms:
                        stopReason = ms.StopReason;
                        usage = ms.Usage;
                        break;
                    case ChatStreamChunk.Error err:
                        errorText = err.Message;
                        break;
                }

                if (stopReason is not null) break;
                if (errorText is not null) break;
            }

            if (errorText is not null)
            {
                if (pendingTextBuffer.Length > 0) assistantBlocks.Add(new ChatContentBlock.TextBlock(pendingTextBuffer.ToString()));
                await _conversationStore.AppendMessageAsync(
                    conversationId, ChatRole.Assistant, assistantBlocks, provider.Kind, conversation.ModelId,
                    usage, ChatStopReason.Error, cancellationToken);
                await _auditPublisher.PublishAsync(
                    AgentEventTopic.TopicName,
                    AgentEventTypes.MessageAssistantFailed,
                    AgentResourceKinds.Message,
                    resource: new { conversationId, messageId = assistantMessageId },
                    details: new { error = errorText, iteration },
                    cancellationToken);
                yield return new AgentEvent.Error(errorText);
                yield return new AgentEvent.Done();
                yield break;
            }

            if (pendingTextBuffer.Length > 0)
            {
                assistantBlocks.Add(new ChatContentBlock.TextBlock(pendingTextBuffer.ToString()));
            }
            foreach (var (toolUseId, info) in toolStarts)
            {
                assistantBlocks.Add(new ChatContentBlock.ToolUseBlock(toolUseId, info.Name, info.Args));
            }

            // Persist the assistant turn before invoking tools so the row
            // exists when we attach tool_call children.
            var persistedAssistantId = await _conversationStore.AppendMessageAsync(
                conversationId, ChatRole.Assistant, assistantBlocks, provider.Kind, conversation.ModelId,
                usage, stopReason, cancellationToken);
            history.Add(new ChatMessage(ChatRole.Assistant, assistantBlocks));

            if (stopReason == ChatStopReason.ToolUse && toolStarts.Count > 0)
            {
                var toolResults = new List<ChatContentBlock>();

                foreach (var (toolUseId, info) in toolStarts)
                {
                    var toolCallId = await _conversationStore.AppendToolCallAsync(
                        persistedAssistantId, toolUseId, info.Name, info.Args, cancellationToken);

                    await _auditPublisher.PublishAsync(
                        AgentEventTopic.TopicName,
                        AgentEventTypes.ToolInvoked,
                        AgentResourceKinds.ToolCall,
                        resource: new { conversationId, messageId = persistedAssistantId, toolCallId, toolUseId },
                        details: new { name = info.Name },
                        cancellationToken);

                    yield return new AgentEvent.ToolStarted(toolCallId, toolUseId, info.Name, info.Args);

                    var sw = Stopwatch.StartNew();
                    JsonElement resultValue;
                    bool isError = false;
                    string? toolErrorText = null;

                    if (!_skillRegistry.TryGetTool(info.Name, out var tool, out _))
                    {
                        isError = true;
                        toolErrorText = $"Unknown tool '{info.Name}'.";
                        resultValue = JsonSerializer.SerializeToElement(new { error = toolErrorText });
                    }
                    else
                    {
                        // Side-channel for events emitted from inside the tool
                        // (currently only the page-query channel emits here).
                        // We pump it concurrently with the tool's await so
                        // PageQueryRequested events reach the SPA in real time.
                        var sideEvents = Channel.CreateUnbounded<AgentEvent>();
                        Func<AgentEvent, ValueTask> emit = ev => sideEvents.Writer.WriteAsync(ev, cancellationToken);
                        _pageQueryChannel.Activate(conversationId, emit);
                        _pageActionChannel.Activate(conversationId, emit);

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ToolTimeoutSeconds));

                        var toolContext = new AgentToolContext(sessionContext, _services);
                        var invokeTask = SafeInvokeToolAsync(
                            tool!, info.Args, toolContext,
                            outerCt: cancellationToken,
                            combinedCt: timeoutCts.Token,
                            timeoutSeconds: _options.ToolTimeoutSeconds);

                        while (!invokeTask.IsCompleted)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            var readWait = sideEvents.Reader.WaitToReadAsync(cancellationToken).AsTask();
                            await Task.WhenAny(invokeTask, readWait).ConfigureAwait(false);
                            while (sideEvents.Reader.TryRead(out var sideEv)) yield return sideEv;
                        }
                        sideEvents.Writer.TryComplete();
                        while (sideEvents.Reader.TryRead(out var sideEv)) yield return sideEv;

                        _pageQueryChannel.Deactivate();
                        _pageActionChannel.Deactivate();

                        var outcome = await invokeTask.ConfigureAwait(false);
                        resultValue = outcome.Result;
                        isError = outcome.IsError;
                        toolErrorText = outcome.ErrorText;
                    }
                    sw.Stop();

                    await _conversationStore.UpdateToolCallAsync(
                        toolCallId,
                        status: isError ? "failed" : "succeeded",
                        result: resultValue,
                        errorText: toolErrorText,
                        durationMs: sw.ElapsedMilliseconds,
                        cancellationToken);

                    var auditEventType = isError ? AgentEventTypes.ToolFailed : AgentEventTypes.ToolCompleted;
                    await _auditPublisher.PublishAsync(
                        AgentEventTopic.TopicName,
                        auditEventType,
                        AgentResourceKinds.ToolCall,
                        resource: new { conversationId, toolCallId, toolUseId },
                        details: new { durationMs = sw.ElapsedMilliseconds, error = toolErrorText },
                        cancellationToken);

                    if (isError)
                    {
                        yield return new AgentEvent.ToolFailed(toolCallId, toolUseId, toolErrorText ?? "Tool failed.", sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        yield return new AgentEvent.ToolCompleted(toolCallId, toolUseId, resultValue, sw.ElapsedMilliseconds);
                    }

                    toolResults.Add(new ChatContentBlock.ToolResultBlock(toolUseId, resultValue, isError));
                }

                // Persist a synthetic "tool" message carrying all the tool_results
                // so the next provider call sees them.
                await _conversationStore.AppendMessageAsync(
                    conversationId, ChatRole.Tool, toolResults, providerKind: null, modelId: null,
                    usage: null, stopReason: null, cancellationToken);
                history.Add(new ChatMessage(ChatRole.Tool, toolResults));

                continue;
            }

            // Either end_turn, max_tokens, or anything else terminal: report
            // and exit.
            await _auditPublisher.PublishAsync(
                AgentEventTopic.TopicName,
                AgentEventTypes.MessageAssistantCompleted,
                AgentResourceKinds.Message,
                resource: new { conversationId, messageId = persistedAssistantId },
                details: new { stopReason = stopReason?.ToString().ToLowerInvariant(), iteration, inputTokens = usage?.InputTokens, outputTokens = usage?.OutputTokens },
                cancellationToken);

            yield return new AgentEvent.MessageCompleted(persistedAssistantId, stopReason ?? ChatStopReason.EndTurn, usage);
            yield return new AgentEvent.Done();
            yield break;
        }

        // Hit the iteration cap.
        yield return new AgentEvent.Error($"Stopped after {_options.MaxIterations} tool-use iterations to avoid runaway recursion.");
        yield return new AgentEvent.Done();
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    // Wraps a tool invocation in the same exception model the loop uses to
    // emit ToolFailed events. Lifted out of the loop body so the loop itself
    // (which yields events) can stay free of try/catch around await.
    private async Task<(JsonElement Result, bool IsError, string? ErrorText)> SafeInvokeToolAsync(
        AgentTool tool,
        JsonElement args,
        AgentToolContext context,
        CancellationToken outerCt,
        CancellationToken combinedCt,
        int timeoutSeconds)
    {
        try
        {
            var result = await tool.Invoke(args, context, combinedCt).ConfigureAwait(false);
            return (result, false, null);
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested)
        {
            const string msg = "Cancelled.";
            return (JsonSerializer.SerializeToElement(new { error = msg }), true, msg);
        }
        catch (OperationCanceledException)
        {
            var msg = $"Tool timed out after {timeoutSeconds}s.";
            return (JsonSerializer.SerializeToElement(new { error = msg }), true, msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {ToolName} threw", tool.Name);
            return (JsonSerializer.SerializeToElement(new { error = ex.Message }), true, ex.Message);
        }
    }

    // Validates and normalizes a SPA-supplied page snapshot into the
    // server-side record skills consume. Returns null (and logs) if the
    // snapshot is unsafe — page key mismatch, oversized, or otherwise
    // malformed. Conversation flow proceeds without page awareness rather
    // than failing the message; the user can retry.
    private PageContextSnapshot? TryNormalizeSnapshot(PageContextInput input, string conversationPageKey)
    {
        if (!string.Equals(input.PageKey, conversationPageKey, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Dropping page snapshot: pageKey mismatch (snapshot={SnapshotKey}, conversation={ConvKey})",
                input.PageKey, conversationPageKey);
            return null;
        }

        var summary = input.Summary;
        if (!string.IsNullOrEmpty(summary) && summary.Length > MaxSnapshotSummaryChars)
        {
            summary = summary.Substring(0, MaxSnapshotSummaryChars - 1) + "…";
        }

        // The endpoint already enforces the size cap on Data; here we
        // re-check for direct callers (tests). Cheap to compute since
        // JsonElement.GetRawText returns a string slice over the underlying
        // buffer.
        try
        {
            var raw = input.Data.GetRawText();
            if (raw.Length > MaxSnapshotDataBytes)
            {
                _logger.LogInformation(
                    "Dropping page snapshot: data exceeds {Cap} bytes (got {Size}).",
                    MaxSnapshotDataBytes, raw.Length);
                return null;
            }
        }
        catch (InvalidOperationException)
        {
            // Disposed/invalid JsonElement — drop snapshot.
            return null;
        }

        return new PageContextSnapshot(
            PageKey: input.PageKey,
            SchemaVersion: input.SchemaVersion,
            Summary: summary,
            Version: input.Version,
            Data: input.Data);
    }
}
