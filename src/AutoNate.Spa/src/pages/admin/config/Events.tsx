import { useEventCatalog } from "@/hooks/useEventCatalog";

export default function Events() {
  const { data, isLoading, isError, error } = useEventCatalog();

  if (isLoading) {
    return (
      <>
        <div className="page-head">
          <h1 className="page-header mb-1">Events</h1>
        </div>
        <p className="text-muted">Loading event catalog…</p>
      </>
    );
  }

  if (isError || !data) {
    return (
      <>
        <div className="page-head">
          <h1 className="page-header mb-1">Events</h1>
        </div>
        <div className="alert alert-danger">
          {error instanceof Error ? error.message : "Failed to load the event catalog."}
        </div>
      </>
    );
  }

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Events</h1>
        <p className="page-head-copy">
          Reference for every event the application publishes to its message bus. Events are
          delivered through Dapr pub/sub; a single live feed across all topics is available on
          the <strong>Bus Watcher</strong> page.
        </p>
      </div>

      {data.transports.map((transport) => (
        <div key={transport.topic} className="panel panel-inverse mb-3">
          <div className="panel-heading">
            <h4 className="panel-title">Transport — {transport.topic}</h4>
          </div>
          <div className="panel-body">
            <dl className="row mb-0">
              <dt className="col-sm-3">Topic</dt>
              <dd className="col-sm-9">
                <code>{transport.topic}</code>
                <div className="text-muted small mt-1">
                  Events on this topic share the schema documented in their category below. The
                  specific kind of event is carried in the <code>eventType</code> field on the
                  payload.
                </div>
              </dd>

              <dt className="col-sm-3">Broker</dt>
              <dd className="col-sm-9">{transport.description}</dd>

              <dt className="col-sm-3">Source</dt>
              <dd className="col-sm-9">{transport.source}</dd>
            </dl>
          </div>
        </div>
      ))}

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Common envelope</h4>
        </div>
        <div className="panel-body">
          <p className="text-muted mb-2">
            Every event — regardless of category — carries this envelope. Category-specific
            fields are documented per category below.
          </p>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th style={{ width: "20%" }}>Field</th>
                  <th style={{ width: "20%" }}>Type</th>
                  <th>Description</th>
                </tr>
              </thead>
              <tbody>
                {data.payloadFields.map((field) => (
                  <tr key={field.name}>
                    <td>
                      <code>{field.name}</code>
                    </td>
                    <td className="text-muted">{field.type}</td>
                    <td>{field.description}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {data.categories.map((category) => (
        <div key={category.title} className="panel panel-inverse mb-3">
          <div className="panel-heading">
            <h4 className="panel-title">{category.title}</h4>
          </div>
          <div className="panel-body">
            <p className="text-muted">{category.description}</p>
            {category.payloadFields.length > 0 && (
              <div className="table-responsive mb-3">
                <table className="table table-sm table-striped mb-0">
                  <thead>
                    <tr>
                      <th style={{ width: "22%" }}>Field</th>
                      <th style={{ width: "20%" }}>Type</th>
                      <th>Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    {category.payloadFields.map((field) => (
                      <tr key={field.name}>
                        <td>
                          <code>{field.name}</code>
                        </td>
                        <td className="text-muted">{field.type}</td>
                        <td>{field.description}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <div className="table-responsive">
              <table className="table table-sm mb-0">
                <thead>
                  <tr>
                    <th style={{ width: "22%" }}>Event</th>
                    <th>What it means / when it fires</th>
                  </tr>
                </thead>
                <tbody>
                  {category.events.map((evt) => (
                    <tr key={`${evt.topic}:${evt.eventType}`}>
                      <td>
                        <code>{evt.eventType}</code>
                      </td>
                      <td>
                        <div>
                          <strong>{evt.summary}</strong>
                        </div>
                        <div className="text-muted small mt-1">{evt.firesWhen}</div>
                        {evt.payloadHighlights.length > 0 && (
                          <ul className="small mt-2 mb-0">
                            {evt.payloadHighlights.map((line, idx) => (
                              <li key={idx}>{line}</li>
                            ))}
                          </ul>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      ))}
    </>
  );
}
