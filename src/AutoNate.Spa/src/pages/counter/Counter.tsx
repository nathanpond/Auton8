import { useState } from "react";

export default function Counter() {
  const [count, setCount] = useState(0);

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Counter</h1>
        <p className="page-head-copy">Trivial counter — preserved from the Blazor demo.</p>
      </div>
      <div className="card card-body">
        <p className="mb-2 fs-5">
          Current count: <strong>{count}</strong>
        </p>
        <div>
          <button type="button" className="btn btn-primary" onClick={() => setCount((n) => n + 1)}>
            Click me
          </button>
        </div>
      </div>
    </>
  );
}
