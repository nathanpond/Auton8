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
    private readonly ConversationCompactor _compactor;
    private readonly Catalog.IAgentModelCatalog _catalog;
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
        ConversationCompactor compactor,
        Catalog.IAgentModelCatalog catalog,
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
        _compactor = compactor;
        _catalog = catalog;
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
        // Header-only lookup — we read PageKey + ConnectionId here and load
        // the real history later via LoadMessagesWithIdsAsync. Calling the
        // full GetForUserAsync would pull every message + tool call (only to
        // throw them away) and emit a spurious ConversationViewed audit on
        // every send.
        var conversation = await _conversationStore.GetHeaderForUserAsync(conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            yield return new AgentEvent.Error("Conversation not found.");
            yield return new AgentEvent.Done();
            yield break;
        }

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

        var loaded = (await _conversationStore.LoadMessagesWithIdsAsync(conversationId, cancellationToken)).ToList();
        var history = new List<ChatMessage>(loaded.Count);
        var historyIds = new List<Guid>(loaded.Count);
        foreach (var lm in loaded)
        {
            history.Add(lm.Message);
            historyIds.Add(lm.Id);
        }

        // Defensive sanitizer: heal any orphan `tool_use` blocks before the
        // history hits the provider. The tool loop persists the assistant
        // message containing tool_use BEFORE running the tool + writing the
        // tool_result message, so a cancellation between those steps (user
        // closes tab mid-tool-call, SSE stream drops, etc.) can leave a
        // tool_use without its matching tool_result. Anthropic responds
        // 400 if it sees that shape, which strands the whole conversation.
        //
        // Walk pairs: every assistant ToolUseBlock with id X must be
        // followed in the NEXT message (Tool role) by a ToolResultBlock with
        // id X. If any are missing, we synthesize an interrupted result so
        // the provider sees well-formed history. The synthetic results are
        // NOT persisted — only injected into this turn's working history,
        // so we don't pollute the durable record with retroactive lies.
        SanitizeOrphanToolUses(history, historyIds);

        // Elide oversized tool_use args / tool_result content from prior
        // user turns to keep replay cost bounded. A single web_fetch_result
        // can dump ~70K tokens into the conversation; without elision, every
        // subsequent turn re-sends that same payload until the context
        // window blows. The most recent user-turn data is already
        // synthesized into the assistant's text reply that immediately
        // follows the tool_result, so the model loses no actionable info
        // when the raw bytes are dropped — only the ability to re-quote
        // verbatim from an earlier fetch. See ElideOversizedHistoryBlobs
        // for the threshold + stub shape.
        ElideOversizedHistoryBlobs(history);
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

        var contextWindow = _catalog.GetContextWindow(provider.ModelId);
        var compactionTriggerTokens = (int)(contextWindow * ConversationCompactor.CompactionTriggerFraction);

        // Set when the previous iteration's request 400'd with a context-
        // overflow error. Tells the next iteration to compact aggressively
        // (TailOverride=2) before retrying. Capped to one retry per
        // SendMessage so we never loop forever on an unrecoverable case.
        var forceOverflowCompaction = false;
        var overflowRetriesUsed = 0;

        var iteration = 0;
        while (iteration < _options.MaxIterations)
        {
            iteration++;

            // Per-iteration compaction check. Tool results that arrived in
            // the previous iteration can swell history mid-turn; checking
            // here (not just before the loop) means we re-summarize before
            // sending the next request. The trimmer below is still the
            // last-resort fallback.
            var compactCheck = ConversationHistoryTrimmer.Trim(
                history, systemPrompt, filteredTools, contextWindow, maxTokens);
            var shouldCompact = forceOverflowCompaction
                || compactCheck.EstimatedInputTokens >= compactionTriggerTokens;
            if (shouldCompact)
            {
                int? tailOverride = forceOverflowCompaction ? 2 : null;
                var (compactedHistory, compactedIds, didCompact) = await TryCompactAsync(
                    conversationId, provider, history, historyIds,
                    systemPrompt, filteredTools,
                    contextWindow, maxTokens,
                    tailOverride,
                    estimatedInputTokensBefore: compactCheck.EstimatedInputTokens,
                    cancellationToken);
                if (didCompact)
                {
                    history = compactedHistory;
                    historyIds = compactedIds;
                }
                forceOverflowCompaction = false;
            }

            // Trim oldest history that doesn't fit. Persisted conversation
            // is untouched — this is just what we send to the provider this
            // turn. Without this the loop blows past Anthropic's 200K cap on
            // long conversations and the request 400s.
            var trimResult = ConversationHistoryTrimmer.Trim(
                history,
                systemPrompt,
                filteredTools,
                contextWindow,
                maxTokens);
            if (trimResult.DroppedCount > 0)
            {
                _logger.LogInformation(
                    "Trimmed {Dropped} oldest message(s) from conversation {ConversationId} to fit context window {ContextWindow} (estimated {EstimatedTokens} of budget {Budget}).",
                    trimResult.DroppedCount, conversationId, contextWindow,
                    trimResult.EstimatedInputTokens, trimResult.EffectiveBudgetTokens);
            }

            var request = new ChatRequest(
                Messages: trimResult.Messages,
                SystemPrompt: systemPrompt,
                Tools: filteredTools,
                ModelId: provider.ModelId,
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
                        details: new { providerKind = provider.Kind, modelId = provider.ModelId, contextWindow },
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
                // Context-overflow recovery: if the provider rejected the
                // request because the prompt was too long, run an aggressive
                // compaction (TailOverride=2) and retry the same iteration.
                // This is the safety net for cases where our token estimate
                // undershot the real count (typically dense JSON in a tool
                // result). Capped to one retry per SendMessageAsync so we
                // never loop on an unrecoverable case.
                if (IsContextOverflowError(errorText) && overflowRetriesUsed < 1)
                {
                    overflowRetriesUsed++;
                    forceOverflowCompaction = true;
                    _logger.LogWarning(
                        "Provider returned context-overflow error on iteration {Iteration} for conversation {ConversationId}; force-compacting and retrying. Error: {Error}",
                        iteration, conversationId, errorText);
                    iteration--;
                    continue;
                }

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
                conversationId, ChatRole.Assistant, assistantBlocks, provider.Kind, provider.ModelId,
                usage, stopReason, cancellationToken);
            history.Add(new ChatMessage(ChatRole.Assistant, assistantBlocks));
            historyIds.Add(persistedAssistantId);

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
                var persistedToolId = await _conversationStore.AppendMessageAsync(
                    conversationId, ChatRole.Tool, toolResults, providerKind: null, modelId: null,
                    usage: null, stopReason: null, cancellationToken);
                history.Add(new ChatMessage(ChatRole.Tool, toolResults));
                historyIds.Add(persistedToolId);

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

    // See the call site (history-load) for the why. Walks the working
    // history in order; whenever an assistant message carries one or more
    // tool_use blocks, the next message must be a Tool message containing a
    // tool_result for every tool_use_id. If any are missing (cancellation
    // between persistence steps, SSE drop, etc.), synthesize an "interrupted"
    // tool_result so the provider sees a paired conversation. Mutates
    // `history` and `historyIds` in place — the injected entries get a
    // sentinel id of Guid.Empty so downstream code that maps ids to
    // persisted rows can skip them.
    internal static void SanitizeOrphanToolUses(List<ChatMessage> history, List<Guid> historyIds)
    {
        for (var i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            if (msg.Role != ChatRole.Assistant) continue;
            var unmatchedToolUseIds = new List<string>();
            foreach (var block in msg.Blocks)
            {
                if (block is ChatContentBlock.ToolUseBlock tu)
                {
                    unmatchedToolUseIds.Add(tu.ToolUseId);
                }
            }
            if (unmatchedToolUseIds.Count == 0) continue;

            // Resolve which tool_use_ids are already paired by the next
            // message (if any). Drop matched ids; anything still in the
            // list is an orphan we need to synthesize for.
            if (i + 1 < history.Count && history[i + 1].Role == ChatRole.Tool)
            {
                var paired = new HashSet<string>(StringComparer.Ordinal);
                foreach (var b in history[i + 1].Blocks)
                {
                    if (b is ChatContentBlock.ToolResultBlock tr) paired.Add(tr.ToolUseId);
                }
                unmatchedToolUseIds.RemoveAll(id => paired.Contains(id));
            }
            if (unmatchedToolUseIds.Count == 0) continue;

            // Build a single synthetic Tool message that carries the
            // missing tool_results. If a real Tool message already exists
            // after this assistant turn (covering OTHER tool_uses), append
            // the synthetic results to that one — otherwise insert a new
            // message between i and i+1.
            var syntheticBlocks = new List<ChatContentBlock>();
            foreach (var id in unmatchedToolUseIds)
            {
                syntheticBlocks.Add(new ChatContentBlock.ToolResultBlock(
                    id,
                    SyntheticInterruptedResult(),
                    IsError: true));
            }

            if (i + 1 < history.Count && history[i + 1].Role == ChatRole.Tool)
            {
                var existing = history[i + 1].Blocks.ToList();
                existing.AddRange(syntheticBlocks);
                history[i + 1] = new ChatMessage(ChatRole.Tool, existing);
            }
            else
            {
                history.Insert(i + 1, new ChatMessage(ChatRole.Tool, syntheticBlocks));
                historyIds.Insert(i + 1, Guid.Empty);
            }
        }
    }

    private static JsonElement SyntheticInterruptedResult()
    {
        using var doc = JsonDocument.Parse(
            "{\"kind\":\"interrupted\",\"error\":\"tool_call_interrupted\",\"message\":\"This tool call was interrupted before its result was recorded (likely because the previous turn's stream ended early). It was not applied.\"}");
        return doc.RootElement.Clone();
    }

    // Tool-use args and tool-result content larger than this (serialized
    // JSON length) get replaced with a small stub on replay. 4 KB is the
    // working bound: real tool_results carry ~100 B (apply_page_action
    // success message) to ~2 KB (inspect_page snapshot summary) of useful
    // structure; web_fetch + huge markdown blobs land well past it. Keeping
    // the bar low means almost every "useful for one turn, dead weight
    // afterwards" blob gets pruned. The orphan sanitizer runs first so
    // we never elide a tool_use that's missing its matching tool_result.
    internal const int OversizedBlobThresholdBytes = 4 * 1024;

    // Replace oversized tool_use args + tool_result content from prior
    // turns with compact stubs. Mutates `history` in place; tool name and
    // tool_use_id are preserved so provider-side pairing stays intact.
    // The original blobs remain in the durable conversation record — only
    // the in-memory replay history is altered.
    //
    // Why elide BOTH sides: a 7 KB markdown arg to apply_page_action is
    // just as costly to replay as a 7 KB tool_result. The action's outcome
    // ("Appended 3 blocks…") is preserved in the matching tool_result
    // (small, kept verbatim), so the model can still reason about what
    // happened even when it can't re-read the original markdown.
    internal static void ElideOversizedHistoryBlobs(List<ChatMessage> history)
    {
        for (var i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            var rewrote = false;
            var newBlocks = new List<ChatContentBlock>(msg.Blocks.Count);
            foreach (var block in msg.Blocks)
            {
                if (block is ChatContentBlock.ToolUseBlock tu)
                {
                    var size = MeasureJsonLength(tu.Args);
                    if (size > OversizedBlobThresholdBytes)
                    {
                        newBlocks.Add(new ChatContentBlock.ToolUseBlock(
                            tu.ToolUseId,
                            tu.Name,
                            ElidedArgsStub(tu.Name, size)));
                        rewrote = true;
                        continue;
                    }
                }
                else if (block is ChatContentBlock.ToolResultBlock tr)
                {
                    var size = MeasureJsonLength(tr.Result);
                    if (size > OversizedBlobThresholdBytes)
                    {
                        newBlocks.Add(new ChatContentBlock.ToolResultBlock(
                            tr.ToolUseId,
                            ElidedResultStub(tr.Result, size),
                            tr.IsError));
                        rewrote = true;
                        continue;
                    }
                }
                newBlocks.Add(block);
            }
            if (rewrote) history[i] = new ChatMessage(msg.Role, newBlocks);
        }
    }

    private static int MeasureJsonLength(JsonElement element)
    {
        // GetRawText avoids the cost of round-tripping through a writer;
        // it returns the underlying span unchanged. For our threshold
        // comparison the byte count of UTF-16 chars is good enough — we
        // don't need exact token counts here, just a stable ordering.
        try { return element.GetRawText().Length; }
        catch { return 0; }
    }

    // Stub the model sees in place of an elided tool_use's args. Keeps
    // the tool name (already on the block) and the original size so the
    // model can decide whether the previous call was "expensive enough
    // to redo" if it really needs the original content.
    private static JsonElement ElidedArgsStub(string toolName, int originalSize)
    {
        var payload = $"{{\"_elided\":true,\"_originalSizeBytes\":{originalSize}," +
                      $"\"_note\":\"args for {JsonEncodedString(toolName)} were elided from replay after the call ran; " +
                      $"the matching tool_result holds the outcome.\"}}";
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    // Stub the model sees in place of an elided tool_result. We try to
    // preserve `kind` from the original so the model knows what TYPE of
    // result was returned (e.g. "web_fetch_result", "apply_page_action_
    // applied") — sometimes that's enough to remember "I already fetched
    // X" without seeing the bytes again.
    private static JsonElement ElidedResultStub(JsonElement original, int originalSize)
    {
        string? kind = null;
        if (original.ValueKind == JsonValueKind.Object &&
            original.TryGetProperty("kind", out var k) &&
            k.ValueKind == JsonValueKind.String)
        {
            kind = k.GetString();
        }
        var kindField = kind is null
            ? string.Empty
            : $"\"_originalKind\":\"{JsonEncodedString(kind)}\",";
        var payload = $"{{\"_elided\":true,{kindField}\"_originalSizeBytes\":{originalSize}," +
                      "\"_note\":\"Result content elided from replay history to keep the context window bounded. " +
                      "The tool ran successfully; if you need the original bytes, re-issue the call.\"}";
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    // Cheap JSON-string escape for our stub payloads. We only need to
    // handle the characters that appear in tool names + kind strings —
    // both produced by us, so the surface is small (no embedded quotes
    // in current names). Belt-and-suspenders against future tool names
    // adding `"` or `\`.
    private static string JsonEncodedString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Heuristic for "the provider rejected our request because it was too
    // long". Matches Anthropic's "prompt is too long" wording and OpenAI's
    // "context_length_exceeded" code so the retry path triggers on either.
    // Kept lenient (substring match, lowercased) since the provider error
    // text isn't part of the contract — the next provider release could
    // reword it slightly.
    private static bool IsContextOverflowError(string? errorText)
    {
        if (string.IsNullOrEmpty(errorText)) return false;
        var lower = errorText.ToLowerInvariant();
        return lower.Contains("prompt is too long")
            || lower.Contains("context_length_exceeded")
            || lower.Contains("maximum context length")
            || lower.Contains("context length")
            || lower.Contains("tokens >") && lower.Contains("maximum");
    }

    // Single shared compaction-and-replace path. Used both by the per-
    // iteration trigger check and by the overflow-retry path (which passes
    // tailOverride=2 to summarize aggressively). Returns the updated
    // history and ids on success; returns the originals + didCompact=false
    // when the compactor refuses (history too short, provider error,
    // throw). Failures are logged but never thrown.
    private async Task<(List<ChatMessage> History, List<Guid> Ids, bool DidCompact)> TryCompactAsync(
        Guid conversationId,
        Providers.IChatProvider provider,
        List<ChatMessage> history,
        List<Guid> historyIds,
        string? systemPrompt,
        IReadOnlyList<ChatTool> filteredTools,
        int contextWindow,
        int maxTokens,
        int? tailOverride,
        int estimatedInputTokensBefore,
        CancellationToken cancellationToken)
    {
        var identities = new List<ConversationCompactor.MessageIdentity>(history.Count);
        for (var i = 0; i < history.Count; i++)
        {
            identities.Add(new ConversationCompactor.MessageIdentity(historyIds[i], history[i]));
        }
        var compactInput = new ConversationCompactor.CompactInput(
            History: history,
            HistoryIds: identities,
            ContextWindowTokens: contextWindow,
            MaxOutputTokens: maxTokens,
            SystemPrompt: systemPrompt,
            Tools: filteredTools,
            TailOverride: tailOverride);

        ConversationCompactor.CompactOutput? compactResult = null;
        try
        {
            compactResult = await _compactor.CompactAsync(provider, compactInput, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compaction threw for conversation {ConversationId}; falling back to trimmer.",
                conversationId);
        }

        if (compactResult is null) return (history, historyIds, false);

        var summaryMessageId = await _conversationStore.AppendSummaryAsync(
            conversationId,
            compactResult.SummaryText,
            compactResult.ReplacesThroughMessageId,
            provider.Kind,
            provider.ModelId,
            compactResult.Usage,
            cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationCompacted,
            AgentResourceKinds.Conversation,
            resource: new { id = conversationId, summaryMessageId },
            details: new
            {
                replacesThroughMessageId = compactResult.ReplacesThroughMessageId,
                prefixCount = compactResult.PrefixCount,
                summaryLength = compactResult.SummaryText.Length,
                contextWindow,
                estimatedInputTokensBefore,
                tailOverride
            },
            cancellationToken);

        _logger.LogInformation(
            "Compacted {PrefixCount} oldest message(s) from conversation {ConversationId} into summary {SummaryMessageId} ({SummaryChars} chars, tailOverride={TailOverride}).",
            compactResult.PrefixCount, conversationId, summaryMessageId, compactResult.SummaryText.Length, tailOverride);

        var newHistory = new List<ChatMessage>(history.Count - compactResult.PrefixCount + 1);
        var newHistoryIds = new List<Guid>(historyIds.Count - compactResult.PrefixCount + 1);
        newHistory.Add(new ChatMessage(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock(compactResult.SummaryText) }));
        newHistoryIds.Add(summaryMessageId);
        for (var i = compactResult.PrefixCount; i < history.Count; i++)
        {
            newHistory.Add(history[i]);
            newHistoryIds.Add(historyIds[i]);
        }
        return (newHistory, newHistoryIds, true);
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
    private PageContextSnapshot? TryNormalizeSnapshot(PageContextInput input, string _conversationPageKey)
    {
        // The snapshot is the user's CURRENT page, not the conversation's
        // original page. A mismatch is expected when the user opened the
        // conversation from another page via "Search every page" — we still
        // forward the snapshot so the model can act on the current view.
        // The conversation's stored pageKey stays as metadata only.

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
