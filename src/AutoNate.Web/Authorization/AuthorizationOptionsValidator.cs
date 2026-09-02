using Microsoft.Extensions.Options;

namespace AutoNate.Web.Authorization;

/// <summary>
/// Refuses to start the host on an authorization configuration that would
/// silently fail open (archived-59). Registered with <c>ValidateOnStart()</c>, so the
/// failure surfaces as a startup crash with the offending keys named, the same
/// way <c>WorkflowBehaviors:CallbackSharedSecret</c> and
/// <c>Yjs:InternalSharedSecret</c> are handled.
/// </summary>
/// <remarks>
/// Two classes of problem:
/// <list type="bullet">
/// <item><description><b>Every environment</b>: an <see cref="AuthorizationOptions.Enforcement"/>
/// value outside <see cref="AuthorizationEnforcement.All"/>. The evaluator
/// compares it with ordinal equality (<c>Enforcement != Full</c>), so
/// <c>"Full"</c>, <c>"FULL"</c> or a typo reads as "not full" and allows every
/// instance write with nothing logged. A wrong value is never intentional, so
/// this is rejected in Development too.</description></item>
/// <item><description><b>Outside Development</b>: enforcement not actually
/// turned on. <c>Enabled=false</c> or <c>Enforcement</c> of <c>off</c> /
/// <c>read-only</c> means grants are stored but (partly or wholly) ignored,
/// which is a deliberate rollout state, not something to reach by omission.</description></item>
/// </list>
/// Flags that are fail-open but legitimate — <see cref="AuthorizationOptions.DryRun"/>
/// and <see cref="AuthorizationOptions.AssignSuperAdminToAllExistingUsers"/> —
/// are startup warnings rather than failures, because both have real
/// bootstrap/rollout uses. See Program.cs.
/// </remarks>
public sealed class AuthorizationOptionsValidator : IValidateOptions<AuthorizationOptions>
{
    private readonly bool _isDevelopment;

    public AuthorizationOptionsValidator(bool isDevelopment)
    {
        _isDevelopment = isDevelopment;
    }

    public ValidateOptionsResult Validate(string? name, AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!AuthorizationEnforcement.IsKnown(options.Enforcement))
        {
            failures.Add(
                $"{AuthorizationOptions.SectionName}:Enforcement must be one of " +
                $"\"{string.Join("\", \"", AuthorizationEnforcement.All)}\" (lower-case, exactly) " +
                $"but was \"{options.Enforcement}\". An unrecognised value is treated as " +
                "\"not full\" by the evaluator, which allows every instance write.");
        }

        if (!_isDevelopment)
        {
            if (!options.Enabled)
            {
                failures.Add(
                    $"{AuthorizationOptions.SectionName}:Enabled must be true outside Development. " +
                    "With it false every permission check returns allow, so any authenticated " +
                    "user can read and mutate every record, grant, plugin and connection.");
            }

            if (options.Enforcement != AuthorizationEnforcement.Full)
            {
                failures.Add(
                    $"{AuthorizationOptions.SectionName}:Enforcement must be " +
                    $"\"{AuthorizationEnforcement.Full}\" outside Development but was " +
                    $"\"{options.Enforcement}\". \"{AuthorizationEnforcement.Off}\" ignores grants " +
                    $"entirely and \"{AuthorizationEnforcement.ReadOnly}\" filters reads while " +
                    "letting every write through. Use Authorization:DryRun for a staged rollout " +
                    "instead — it keeps the checks running and logs would-be denials.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
