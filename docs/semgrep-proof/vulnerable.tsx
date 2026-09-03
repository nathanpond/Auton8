// DELIBERATELY VULNERABLE. Detection proof for #66's Semgrep workflow.
export function Proof({ c }) {
  return <div dangerouslySetInnerHTML={{ __html: c }} />;
}
