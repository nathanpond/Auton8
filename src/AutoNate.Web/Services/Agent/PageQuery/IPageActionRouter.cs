namespace AutoNate.Web.Services.Agent.PageQuery;

// Singleton bridge between the in-flight agent loop and the
// page-action-results POST endpoint. Same shape as IPageQueryRouter — kept
// in a parallel type so the two channels don't accidentally route through
// each other.
public interface IPageActionRouter
{
    System.Threading.Tasks.TaskCompletionSource<PageActionResult> Register(Guid conversationId, string actionId);
    void Cleanup(Guid conversationId, string actionId);
    bool TryResolve(Guid conversationId, string actionId, PageActionResult result);
}
