using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// <see cref="IFlowableJobClient"/> over Flowable's management REST service (#172).
/// </summary>
public sealed class FlowableJobClient(HttpClient httpClient) : IFlowableJobClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The engine paginates every collection. 200 matches
    /// <c>WorkflowExecutionQuerySize</c>, and the cap is real rather than
    /// defensive: a stuck list is meant to be read, and a thousand rows is a
    /// symptom of something a list cannot fix.
    /// </summary>
    private const int PageSize = 200;

    private readonly HttpClient _httpClient = httpClient;

    /// <summary>The collection path each queue lives at.</summary>
    private static string CollectionFor(WorkflowJobQueue queue) => queue switch
    {
        WorkflowJobQueue.Executable => "service/management/jobs",
        WorkflowJobQueue.Timer => "service/management/timer-jobs",
        WorkflowJobQueue.DeadLetter => "service/management/deadletter-jobs",
        WorkflowJobQueue.Suspended => "service/management/suspended-jobs",
        _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, "Unknown job queue.")
    };

    private static readonly WorkflowJobQueue[] AllQueues =
    [
        WorkflowJobQueue.DeadLetter,
        WorkflowJobQueue.Executable,
        WorkflowJobQueue.Timer,
        WorkflowJobQueue.Suspended
    ];

    public async Task<IReadOnlyList<WorkflowJobSummary>> GetJobsForExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        var all = new List<WorkflowJobSummary>();

        foreach (var queue in AllQueues)
        {
            // #319's note, verified again here: /management/deadletter-jobs
            // IGNORES a processInstanceId query parameter and returns everything.
            // Filtering client-side is therefore not belt-and-braces -- without
            // it this method would return another execution's stuck jobs under
            // this execution's heading, which is worse than returning none.
            var page = await FetchAsync(queue, query: null, cancellationToken);
            all.AddRange(page.Where(job => string.Equals(
                job.ProcessInstanceId, processInstanceId, StringComparison.Ordinal)));
        }

        return Ordered(all);
    }

    public async Task<IReadOnlyList<WorkflowJobSummary>> GetJobsAsync(
        bool deadLetteredOnly, CancellationToken cancellationToken = default)
    {
        var queues = deadLetteredOnly ? [WorkflowJobQueue.DeadLetter] : AllQueues;
        var all = new List<WorkflowJobSummary>();

        foreach (var queue in queues)
        {
            all.AddRange(await FetchAsync(queue, query: null, cancellationToken));
        }

        return Ordered(all);
    }

    public async Task<string?> GetJobExceptionStackAsync(
        string jobId, WorkflowJobQueue queue, CancellationToken cancellationToken = default)
    {
        var url = $"{CollectionFor(queue)}/{Uri.EscapeDataString(jobId)}/exception-stacktrace";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // The engine answers 404 both for "no such job" and for "this job kept no
        // stacktrace". Neither is an error worth throwing over on a read whose
        // whole purpose is optional detail -- a null says "nothing to show", and
        // the caller already knows the job exists because it listed it.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(response, "read the job's exception stacktrace");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    public async Task RetryJobAsync(
        string jobId, WorkflowJobQueue queue, CancellationToken cancellationToken = default)
    {
        // Read out of JobResource, not recalled. Each queue accepts a DIFFERENT
        // verb, and the engine rejects the others by name:
        //   jobs            -> "Invalid action, only 'execute' is supported."
        //   timer-jobs      -> "only 'move' or 'reschedule'"
        //   deadletter-jobs -> "only 'move' or 'moveToHistoryJob'"
        var action = queue switch
        {
            WorkflowJobQueue.Executable => "execute",
            WorkflowJobQueue.Timer or WorkflowJobQueue.DeadLetter => "move",
            WorkflowJobQueue.Suspended => throw new FlowableRequestException(
                HttpStatusCode.BadRequest,
                "retry the job",
                "This job's process is suspended. Resume the workflow first — retrying "
                + "would put work back in front of an engine that will not run it."),
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, "Unknown job queue.")
        };

        var url = $"{CollectionFor(queue)}/{Uri.EscapeDataString(jobId)}";
        using var response = await _httpClient.PostAsJsonAsync(
            url, new JobActionRequest { Action = action }, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "retry the job");
    }

    public async Task RescheduleTimerJobAsync(
        string jobId, DateTimeOffset dueAtUtc, CancellationToken cancellationToken = default)
    {
        var url = $"service/management/timer-jobs/{Uri.EscapeDataString(jobId)}";
        using var response = await _httpClient.PostAsJsonAsync(
            url,
            new JobActionRequest
            {
                Action = "reschedule",
                // "Reschedule timer actions must have a valid due date" -- the
                // engine's own words, and it parses ISO-8601. Sent as UTC so a
                // server in a different timezone cannot shift it.
                DueDate = dueAtUtc.ToUniversalTime().ToString("O")
            },
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, "reschedule the timer");
    }

    private async Task<IReadOnlyList<WorkflowJobSummary>> FetchAsync(
        WorkflowJobQueue queue, string? query, CancellationToken cancellationToken)
    {
        var url = $"{CollectionFor(queue)}?size={PageSize}";
        if (!string.IsNullOrWhiteSpace(query)) url += "&" + query;

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, $"list {Describe(queue)} jobs");

        var payload = await DeserializeAsync<JobListResponse>(response, cancellationToken);
        return payload.Data.Select(job => ToSummary(job, queue)).ToList();
    }

    private static string Describe(WorkflowJobQueue queue) => queue switch
    {
        WorkflowJobQueue.Executable => "executable",
        WorkflowJobQueue.Timer => "timer",
        WorkflowJobQueue.DeadLetter => "dead-lettered",
        WorkflowJobQueue.Suspended => "suspended",
        _ => "unknown"
    };

    /// <summary>
    /// Dead-lettered first, then by due date.
    /// </summary>
    /// <remarks>
    /// The ordering is the point of the list. An operator opens it because
    /// something is wrong, so what is already broken outranks what is merely
    /// scheduled — and within that, soonest first, because a timer due in a
    /// minute is more actionable than one due next month.
    /// </remarks>
    private static IReadOnlyList<WorkflowJobSummary> Ordered(IEnumerable<WorkflowJobSummary> jobs) =>
        jobs.OrderBy(job => job.Queue == WorkflowJobQueue.DeadLetter ? 0 : 1)
            .ThenBy(job => job.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(job => job.Id, StringComparer.Ordinal)
            .ToList();

    private static WorkflowJobSummary ToSummary(JobResponse job, WorkflowJobQueue queue) => new()
    {
        Id = job.Id ?? string.Empty,
        Queue = queue,
        ProcessInstanceId = job.ProcessInstanceId,
        ProcessDefinitionId = job.ProcessDefinitionId,
        ElementId = job.ElementId,
        ElementName = job.ElementName,
        Retries = job.Retries,
        ExceptionMessage = job.ExceptionMessage,
        DueAtUtc = job.DueDate,
        CreatedAtUtc = job.CreateTime
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        var details = string.IsNullOrWhiteSpace(body) ? "No response body was returned." : body;

        throw new FlowableRequestException(
            response.StatusCode,
            operation,
            $"Flowable could not {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Flowable returned an empty job payload.");
    }

    private sealed class JobListResponse
    {
        public List<JobResponse> Data { get; init; } = [];

        public int Total { get; init; }
    }

    private sealed class JobResponse
    {
        public string? Id { get; init; }

        public string? ProcessInstanceId { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public string? ElementId { get; init; }

        public string? ElementName { get; init; }

        public int Retries { get; init; }

        public string? ExceptionMessage { get; init; }

        public DateTimeOffset? DueDate { get; init; }

        public DateTimeOffset? CreateTime { get; init; }
    }

    private sealed class JobActionRequest
    {
        public string Action { get; init; } = string.Empty;

        /// <summary>Only sent for a reschedule; omitted otherwise, not sent null.</summary>
        public string? DueDate { get; init; }
    }
}
