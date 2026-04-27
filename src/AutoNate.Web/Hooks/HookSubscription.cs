using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Hooks;

internal readonly record struct HookSubscription<TDelegate>(
    HookHandle Handle,
    int Priority,
    long RegistrationOrder,
    TDelegate Callback) where TDelegate : Delegate;
