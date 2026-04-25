import { FieldDataType, OptionChoice } from "@/types/records";

type Props = {
  dataType: FieldDataType;
  config: Record<string, unknown>;
  onChange: (next: Record<string, unknown>) => void;
};

export default function FieldConfigPanel({ dataType, config, onChange }: Props) {
  switch (dataType) {
    case "text":
      return <TextConfig config={config} onChange={onChange} />;
    case "number":
      return <NumberConfig config={config} onChange={onChange} />;
    case "date":
      return <DateConfig config={config} onChange={onChange} />;
    case "phone":
      return <PhoneConfig config={config} onChange={onChange} />;
    case "email":
      return <p className="form-text mb-0">No additional configuration.</p>;
    case "option":
      return <OptionConfig config={config} onChange={onChange} />;
    case "boolean":
      return <p className="form-text mb-0">No additional configuration.</p>;
    default:
      return (
        <p className="form-text mb-0 text-warning">
          Unknown data type; config will be sent as-is.
        </p>
      );
  }
}

function TextConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "single";
  const maxLength = Number(config.maxLength ?? 4000);
  return (
    <>
      <div className="mb-3">
        <label className="form-label">Variant</label>
        <select
          className="form-select"
          value={variant}
          onChange={(e) => onChange({ ...config, variant: e.target.value })}
        >
          <option value="single">Single-line</option>
          <option value="multi">Multi-line</option>
        </select>
      </div>
      <div className="mb-3">
        <label className="form-label">Max length</label>
        <input
          type="number"
          className="form-control"
          min={1}
          max={65536}
          value={maxLength}
          onChange={(e) => onChange({ ...config, maxLength: Number(e.target.value) })}
        />
      </div>
    </>
  );
}

function NumberConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "decimal";
  const precision = Number(config.precision ?? 2);
  const min = config.min === null || config.min === undefined ? "" : String(config.min);
  const max = config.max === null || config.max === undefined ? "" : String(config.max);

  return (
    <>
      <div className="mb-3">
        <label className="form-label">Variant</label>
        <select
          className="form-select"
          value={variant}
          onChange={(e) => onChange({ ...config, variant: e.target.value })}
        >
          <option value="decimal">Decimal</option>
          <option value="integer">Integer</option>
        </select>
      </div>
      {variant === "decimal" && (
        <div className="mb-3">
          <label className="form-label">Precision (decimal places)</label>
          <input
            type="number"
            className="form-control"
            min={0}
            max={12}
            value={precision}
            onChange={(e) => onChange({ ...config, precision: Number(e.target.value) })}
          />
        </div>
      )}
      <div className="row g-2 mb-3">
        <div className="col">
          <label className="form-label">Min</label>
          <input
            type="number"
            className="form-control"
            value={min}
            onChange={(e) =>
              onChange({ ...config, min: e.target.value === "" ? null : Number(e.target.value) })
            }
          />
        </div>
        <div className="col">
          <label className="form-label">Max</label>
          <input
            type="number"
            className="form-control"
            value={max}
            onChange={(e) =>
              onChange({ ...config, max: e.target.value === "" ? null : Number(e.target.value) })
            }
          />
        </div>
      </div>
    </>
  );
}

function DateConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "date";
  return (
    <div className="mb-3">
      <label className="form-label">Variant</label>
      <select
        className="form-select"
        value={variant}
        onChange={(e) => onChange({ ...config, variant: e.target.value })}
      >
        <option value="date">Date only</option>
        <option value="datetime">Date &amp; time</option>
        <option value="range">Date range</option>
      </select>
    </div>
  );
}

function PhoneConfig({ config, onChange }: Omit<Props, "dataType">) {
  const region = (config.region as string) ?? "US";
  return (
    <div className="mb-3">
      <label className="form-label">Default region (ISO country code)</label>
      <input
        type="text"
        className="form-control"
        maxLength={3}
        value={region}
        onChange={(e) => onChange({ ...config, region: e.target.value.toUpperCase() })}
      />
    </div>
  );
}

function OptionConfig({ config, onChange }: Omit<Props, "dataType">) {
  const multi = Boolean(config.multi);
  const choices = Array.isArray(config.choices) ? (config.choices as OptionChoice[]) : [];

  const updateChoice = (index: number, patch: Partial<OptionChoice>) => {
    const next = choices.map((c, i) => (i === index ? { ...c, ...patch } : c));
    onChange({ ...config, choices: next });
  };

  const addChoice = () => {
    const next = [...choices, { value: `option_${choices.length + 1}`, label: `Option ${choices.length + 1}` }];
    onChange({ ...config, choices: next });
  };

  const removeChoice = (index: number) => {
    onChange({ ...config, choices: choices.filter((_, i) => i !== index) });
  };

  return (
    <>
      <div className="form-check form-switch mb-3">
        <input
          type="checkbox"
          className="form-check-input"
          id="option-multi"
          checked={multi}
          onChange={(e) => onChange({ ...config, multi: e.target.checked })}
        />
        <label className="form-check-label" htmlFor="option-multi">
          Allow multiple selections
        </label>
      </div>
      <label className="form-label">Choices</label>
      <div className="vstack gap-2 mb-2">
        {choices.map((choice, index) => (
          <div key={index} className="input-group">
            <span className="input-group-text" style={{ minWidth: "4rem" }}>
              value
            </span>
            <input
              className="form-control"
              value={choice.value}
              onChange={(e) => updateChoice(index, { value: e.target.value })}
            />
            <span className="input-group-text" style={{ minWidth: "4rem" }}>
              label
            </span>
            <input
              className="form-control"
              value={choice.label}
              onChange={(e) => updateChoice(index, { label: e.target.value })}
            />
            <button
              type="button"
              className="btn btn-outline-danger"
              onClick={() => removeChoice(index)}
              aria-label="Remove choice"
            >
              <i className="fa fa-trash"></i>
            </button>
          </div>
        ))}
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={addChoice}>
        <i className="fa fa-plus me-1"></i>Add choice
      </button>
    </>
  );
}
