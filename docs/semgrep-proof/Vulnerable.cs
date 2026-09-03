// DELIBERATELY VULNERABLE. Detection proof for #66's Semgrep workflow.
// Lives under docs/ so no project glob compiles it. Deleted once the alerts
// have been observed in code scanning.
//
// No credential literal here: GitHub's own push protection rejects one, which
// is the correct behaviour and not something to bypass for a test. The
// p/secrets pack was proven locally instead.
using System.Data.SqlClient;

public class SemgrepDetectionProof
{
    public void Lookup(SqlConnection conn, string userInput)
    {
        var cmd = new SqlCommand("SELECT * FROM users WHERE name = '" + userInput + "'", conn);
        cmd.ExecuteNonQuery();
    }
}
