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
          Reference for every event the application publishes to its message bus. All events
          below are produced by the Flowable workflow engine extension and delivered through
          Dapr pub/sub; a single live feed of these events is available on the{" "}
          <strong>Bus Watcher</strong> page.
        </p>
      </div>

      {data.transports.map((transport) => (
        <div key={transport.topic} className="panel panel-inverse mb-3">
          <div className="panel-heading">
            <h4 className="panel-title">Transport</h4>
          </div>
          <div className="panel-body">
            <dl className="row mb-0">
              <dt className="col-sm-3">Topic</dt>
              <dd className="col-sm-9">
                <code>{transport.topic}</code>
                <div className="text-muted small mt-1">
                  Every event below is published to this topic. The specific kind of event is
                  carried in the <code>eventType</code> field on the payload.
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
          <h4 className="panel-title">Common payload</h4>
        </div>
        <div className="panel-body">
          <p className="text-muted mb-2">
            Every event shares the same payload shape. Fields not relevant to a particular
            event type are omitted (sent as <code>null</code>).
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
