import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useRecordSearch } from "@/hooks/useRecords";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import { SearchFilterClause, SearchRecordsRequest } from "@/types/records";
import "./fields/renderers"; // ensure renderers register on first import
import { getRenderer } from "./fields/registry";
import RecordFilterBuilder from "./RecordFilterBuilder";

export default function RecordList() {
  const { typeShortCode = "" } = useParams<{ typeShortCode: string }>();
  const navigate = useNavigate();
  const code = typeShortCode.toUpperCase();

  const { data: types = [], isLoading: loadingTypes } = useRecordTypes(true);
  const type = types.find((t) => t.shortCode === code) ?? null;

  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [includeArchived, setIncludeArchived] = useState(false);
  const [sort, setSort] = useState<string>("updated_desc");
  const [filters, setFilters] = useState<SearchFilterClause[]>([]);
  const [filtersOpen, setFiltersOpen] = useState(false);

  const { data: fields = [] } = useRecordTypeFields(type?.id ?? null, false);
  const visibleFields = fields.filter((f) => !f.isArchived).slice(0, 4);

  const searchRequest = useMemo<SearchRecordsRequest>(
    () => ({
      recordTypeId: type?.id ?? "",
      filters,
      includeArchived,
      page,
      pageSize,
      sort
    }),
    [type?.id, filters, includeArchived, page, pageSize, sort]
  );

  const { data: searchPage, isLoading: loadingRecords } = useRecordSearch(
    searchRequest,
    Boolean(type?.id)
  );

  if (!loadingTypes && !type) {
    return (
      <div className="page-head">
        <h1 className="page-header mb-1">Records</h1>
        <p className="page-head-copy">
          Unknown record type code <code>{code}</code>.{" "}
          <Link to="/record-types">Browse record types</Link>.
        </p>
      </div>
    );
  }

  const totalCount = searchPage?.totalCount ?? 0;
  const filtersActive = filters.length > 0;

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start">
        <div>
          <h1 className="page-header mb-1">
            <code className="me-2">{code}</code>
            {type?.name ?? "Records"}
          </h1>
          <p className="page-head-copy mb-0">
            <Link to={`/record-types/${type?.id ?? ""}`}>Edit type definition</Link>
          </p>
        </div>
        <div>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => navigate(`/records/${code}/new`)}
            disabled={!type}
          >
            <i className="fa fa-plus me-2"></i>New {type?.name ?? "Record"}
          </button>
        </div>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-body">
          <div className="d-flex justify-content-between align-items-center mb-3 gap-3 flex-wrap">
            <div className="d-flex align-items-center gap-3">
              <button
                type="button"
                className={`btn btn-sm ${filtersActive ? "btn-primary" : "btn-outline-secondary"}`}
                onClick={() => setFiltersOpen((o) => !o)}
              >
                <i className="fa fa-filter me-2"></i>
                Filters
                {filtersActive && (
                  <span className="badge bg-light text-dark ms-2">{filters.length}</span>
                )}
              </button>
              <label className="d-flex align-items-center gap-2 mb-0 small">
                Sort:
                <select
                  className="form-select form-select-sm"
                  value={sort}
                  onChange={(e) => setSort(e.target.value)}
                >
                  <option value="updated_desc">Updated (newest)</option>
                  <option value="created_desc">Created (newest)</option>
                  <option value="key_asc">Key ascending</option>
                  <option value="key_desc">Key descending</option>
                  <option value="name_asc">Name A-Z</option>
                  <option value="name_desc">Name Z-A</option>
                </select>
              </label>
              <div className="form-check form-switch mb-0">
                <input
                  type="checkbox"
                  className="form-check-input"
                  id="include-archived-records"
                  checked={includeArchived}
                  onChange={(e) => {
                    setIncludeArchived(e.target.checked);
                    setPage(0);
                  }}
                />
                <label className="form-check-label small" htmlFor="include-archived-records">
                  Show archived
                </label>
              </div>
            </div>
            <div className="text-body text-opacity-75 small">
              {searchPage
                ? `${totalCount} record${totalCount === 1 ? "" : "s"}${filtersActive ? " match" : ""}`
                : ""}
            </div>
          </div>

          {filtersOpen && (
            <div className="border rounded p-3 mb-3">
              <RecordFilterBuilder
                fields={fields}
                initialFilters={filters}
                onApply={(applied) => {
                  setFilters(applied);
                  setPage(0);
                }}
                onClear={() => {
                  setFilters([]);
                  setPage(0);
                }}
              />
            </div>
          )}

          <div className="table-responsive">
            <table className="table table-striped table-bordered align-middle">
              <thead>
                <tr>
                  <th style={{ width: "8rem" }}>Key</th>
                  <th>Name</th>
                  {visibleFields.map((f) => (
                    <th key={f.id}>{f.displayName}</th>
                  ))}
                  <th style={{ width: "11rem" }}>Updated</th>
                  <th style={{ width: "6rem" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {loadingRecords && (
                  <tr>
                    <td colSpan={4 + visibleFields.length} className="text-center text-body text-opacity-50 p-4">
                      Loading...
                    </td>
                  </tr>
                )}
                {!loadingRecords && (searchPage?.items.length ?? 0) === 0 && (
                  <tr>
                    <td colSpan={4 + visibleFields.length} className="text-center text-body text-opacity-50 p-4">
                      {filtersActive ? (
                        <>
                          No records match your filters.{" "}
                          <button
                            type="button"
                            className="btn btn-link p-0 align-baseline"
                            onClick={() => {
                              setFilters([]);
                              setPage(0);
                            }}
                          >
                            Clear filters
                          </button>
                        </>
                      ) : (
                        <>
                          No records yet.{" "}
                          <button
                            type="button"
                            className="btn btn-link p-0 align-baseline"
                            onClick={() => navigate(`/records/${code}/new`)}
                          >
                            Create the first one
                          </button>
                          .
                        </>
                      )}
                    </td>
                  </tr>
                )}
                {searchPage?.items.map((rec) => (
                  <tr key={rec.id} className={rec.isArchived ? "text-body text-opacity-50" : undefined}>
                    <td>
                      <Link to={`/record/${rec.key}`}>
                        <code>{rec.key}</code>
                      </Link>
                    </td>
                    <td>{rec.name}</td>
                    {visibleFields.map((f) => {
                      const renderer = getRenderer(f.dataType);
                      const formatted = renderer
                        ? renderer.formatValue(f, rec.values[f.fieldKey])
                        : String(rec.values[f.fieldKey] ?? "");
                      return <td key={f.id}>{formatted}</td>;
                    })}
                    <td>{formatWhen(rec.updatedAtUtc)}</td>
                    <td>
                      {rec.isArchived ? (
                        <span className="badge bg-secondary">Archived</span>
                      ) : (
                        <span className="badge bg-success">Active</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {searchPage && totalCount > pageSize && (
            <div className="d-flex justify-content-between align-items-center mt-3">
              <label className="d-flex align-items-center gap-2 mb-0 small">
                Page size:
                <select
                  className="form-select form-select-sm"
                  value={pageSize}
                  onChange={(e) => {
                    setPageSize(Number(e.target.value));
                    setPage(0);
                  }}
                >
                  {[10, 25, 50, 100].map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
              </label>
              <nav aria-label="Pagination">
                <ul className="pagination pagination-sm mb-0">
                  <li className={`page-item ${page === 0 ? "disabled" : ""}`}>
                    <button
                      type="button"
                      className="page-link"
                      onClick={() => setPage((p) => Math.max(0, p - 1))}
                      disabled={page === 0}
                    >
                      Previous
                    </button>
                  </li>
                  <li className="page-item disabled">
                    <span className="page-link">
                      Page {page + 1} of {Math.max(1, Math.ceil(totalCount / pageSize))}
                    </span>
                  </li>
                  <li
                    className={`page-item ${(page + 1) * pageSize >= totalCount ? "disabled" : ""}`}
                  >
                    <button
                      type="button"
                      className="page-link"
                      onClick={() => setPage((p) => p + 1)}
                      disabled={(page + 1) * pageSize >= totalCount}
                    >
                      Next
                    </button>
                  </li>
                </ul>
              </nav>
            </div>
          )}
        </div>
      </div>
    </>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
