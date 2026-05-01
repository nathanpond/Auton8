using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Tests.SamplePlugin;

// Test fixture: registers a single filter that always denies the authorize
// hook with reason "sample-plugin". The loader test asserts that an enable
// makes this filter live and a disable revokes it.
public sealed class SamplePlugin : IAutoNatePlugin
{
    public string Name => "AutoNate.Web.Tests.SamplePlugin";
    public string Version => "1.0.0";

    public void Configure(IPluginContext context)
    {
        context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize,
            priority: 10,
            (ctx, _, _) => Task.FromResult(ctx with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = AuthEffectDto.Deny,
                    Reason = "sample-plugin"
                }
            }));
    }
}
