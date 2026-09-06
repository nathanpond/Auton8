// The host API a BPMN script task sees (#147).
//
// Two requirements shape this into a registry rather than a hard-coded
// `variables` global:
//
//  - adding a helper later must be an addition here, not a change to the
//    transport or the runner;
//  - the same surface must be describable as tool definitions, because M8's
//    LLM front-end binds to it.
//
// Both are served by declaring each operation once, with its in-isolate
// implementation alongside its description and parameters. `buildFacadeSource`
// generates the globals the author's script sees; `toToolDefinitions` generates
// the machine-readable surface. Neither can drift from the other, because
// there is only one declaration.
//
// The implementations are SOURCE, evaluated inside the isolate, deliberately.
// Injecting host callbacks across the isolated-vm boundary would put host
// functions within reach of author code; keeping the façade entirely inside the
// isolate means the boundary is only ever crossed by JSON. See scriptTaskRunner.

export type HostParameterType = "string" | "any";

export interface HostParameter {
  name: string;
  type: HostParameterType;
  description: string;
  required: boolean;
}

export interface HostOperationDefinition {
  /** Object the operation hangs off in author code, e.g. `variables`. */
  namespace: string;
  /** Method name within the namespace, e.g. `get`. */
  name: string;
  description: string;
  parameters: HostParameter[];
  /**
   * A JS function expression evaluated inside the isolate. It closes over
   * `__state`, the run's private bookkeeping object. It must not reference
   * anything from the host.
   */
  source: string;
}

/** A tool definition in the shape an LLM function-calling API expects. */
export interface ToolDefinition {
  name: string;
  description: string;
  parameters: {
    type: "object";
    properties: Record<string, { type: string; description: string }>;
    required: string[];
  };
}

export class HostApiRegistry {
  readonly #operations: HostOperationDefinition[] = [];

  register(operation: HostOperationDefinition): this {
    const qualified = `${operation.namespace}.${operation.name}`;
    if (this.#operations.some((o) => `${o.namespace}.${o.name}` === qualified)) {
      throw new Error(`Host operation '${qualified}' is already registered.`);
    }
    if (!/^[A-Za-z_$][\w$]*$/.test(operation.namespace) || !/^[A-Za-z_$][\w$]*$/.test(operation.name)) {
      // The names are interpolated into generated source, so anything that is
      // not a plain identifier is a code-injection vector rather than a typo.
      throw new Error(`Host operation '${qualified}' is not a valid identifier pair.`);
    }
    this.#operations.push(operation);
    return this;
  }

  all(): readonly HostOperationDefinition[] {
    return this.#operations;
  }

  namespaces(): string[] {
    return Array.from(new Set(this.#operations.map((o) => o.namespace)));
  }

  /**
   * Generates the `const <namespace> = { ... }` declarations the author's
   * script runs against. Called once per run, inside the isolate.
   */
  buildFacadeSource(): string {
    return this.namespaces()
      .map((namespace) => {
        const members = this.#operations
          .filter((o) => o.namespace === namespace)
          .map((o) => `  ${o.name}: ${o.source}`)
          .join(",\n");
        return `const ${namespace} = Object.freeze({\n${members}\n});`;
      })
      .join("\n");
  }

  toToolDefinitions(): ToolDefinition[] {
    return this.#operations.map((o) => ({
      name: `${o.namespace}_${o.name}`,
      description: o.description,
      parameters: {
        type: "object" as const,
        properties: Object.fromEntries(
          o.parameters.map((p) => [
            p.name,
            { type: p.type === "any" ? "object" : p.type, description: p.description },
          ])
        ),
        required: o.parameters.filter((p) => p.required).map((p) => p.name),
      },
    }));
  }
}

/**
 * The v1.0 surface: process variables and nothing else. Helpers are a
 * deliberate later addition — the point of the registry is that adding one
 * touches this file only.
 */
export function defaultHostApi(): HostApiRegistry {
  return new HostApiRegistry()
    .register({
      namespace: "variables",
      name: "get",
      description:
        "Reads a process variable by name. Returns undefined when the variable is not set.",
      parameters: [
        { name: "name", type: "string", description: "The process variable's name.", required: true },
      ],
      // Reads come from the snapshot the host sent, overlaid with writes made
      // earlier in this same script — otherwise a set followed by a get in one
      // script would not see its own write.
      source: `function (name) {
        if (Object.prototype.hasOwnProperty.call(__state.mutations, name)) {
          return __state.mutations[name];
        }
        return __state.variables[name];
      }`,
    })
    .register({
      namespace: "variables",
      name: "set",
      description:
        "Writes a process variable. The value is applied to the execution after the script returns, and is visible to later steps.",
      parameters: [
        { name: "name", type: "string", description: "The process variable's name.", required: true },
        { name: "value", type: "any", description: "The value to store.", required: true },
      ],
      source: `function (name, value) {
        if (typeof name !== "string" || name.length === 0) {
          throw new Error("variables.set requires a non-empty variable name.");
        }
        __state.mutations[name] = value;
        return value;
      }`,
    });
}
