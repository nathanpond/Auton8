using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoNate.Plugins.HelloPlugin;

// Demo plugin. Subscribes to autonate.authorize as a pass-through filter that
// only logs — proves the plugin loaded and is being invoked without changing
// any authorization decisions.
public sealed class HelloPlugin : IAutoNatePlugin
{
    public string Name => "HelloPlugin";
    public string Version => "1.0.0";

    public void Configure(IHookRegistrar registrar, IServiceProvider hostServices)
    {
        var logger = hostServices.GetService<ILoggerFactory>()?.CreateLogger("HelloPlugin");

        registrar.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize,
            priority: 100,
            (ctx, _, _) =>
            {
                logger?.LogInformation(
                    "HelloPlugin saw authorize: action={Action} target={Kind}:{Id} effect={Effect}",
                    ctx.Action, ctx.Target.Kind, ctx.Target.Id, ctx.CurrentDecision.Effect);
                return Task.FromResult(ctx);
            });
    }
}
