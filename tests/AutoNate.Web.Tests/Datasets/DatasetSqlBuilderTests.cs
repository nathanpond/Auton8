using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Xunit;

namespace AutoNate.Web.Tests.Datasets;

public sealed class DatasetSqlBuilderTests
{
    private static readonly IReadOnlyList<QueryColumn> AiRiskSchema = new[]
    {
        new QueryColumn("category", QueryDataType.String, IsAggregable: true, IsSystem: false),
        new QueryColumn("severity", QueryDataType.Number, IsAggregable: true, IsSystem: false),
        new QueryColumn("title", QueryDataType.String, IsAggregable: false, IsSystem: false),
    };

    [Fact]
    public void Columns_With_Count_Aggregate_And_Group_Emits_GroupBy_And_Count_Projection()
    {
        var query = AqlParser.Parse(
            "FROM DataSet(\"AIRisk\") COLUMNS(category, COUNT()) GROUP(category)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Equal(
            "SELECT \"category\" AS \"category\", COUNT(*) AS \"COUNT()\" " +
            "FROM \"ds_aiRisk\".\"rows\" GROUP BY \"category\"",
            built.Command.CommandText);
        Assert.Equal(2, built.Projection.Count);
        Assert.Equal("category", built.Projection[0].DisplayName);
        Assert.Equal(QueryDataType.String, built.Projection[0].DataType);
        Assert.Equal("COUNT()", built.Projection[1].DisplayName);
        Assert.Equal(QueryDataType.Number, built.Projection[1].DataType);
    }

    [Fact]
    public void Columns_Without_Aggregates_Projects_Each_Column()
    {
        var query = AqlParser.Parse("FROM DataSet(\"AIRisk\") COLUMNS(category, title)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Equal(
            "SELECT \"category\" AS \"category\", \"title\" AS \"title\" " +
            "FROM \"ds_aiRisk\".\"rows\"",
            built.Command.CommandText);
    }

    [Fact]
    public void No_Columns_No_Group_Falls_Back_To_Star()
    {
        var query = AqlParser.Parse("FROM DataSet(\"AIRisk\")");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Equal("SELECT * FROM \"ds_aiRisk\".\"rows\"", built.Command.CommandText);
        Assert.Empty(built.Projection);
    }

    [Fact]
    public void Group_Without_Columns_Projects_Group_Columns()
    {
        var query = AqlParser.Parse("FROM DataSet(\"AIRisk\") GROUP(category)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Equal(
            "SELECT \"category\" AS \"category\" FROM \"ds_aiRisk\".\"rows\" GROUP BY \"category\"",
            built.Command.CommandText);
    }

    [Fact]
    public void Aggregates_Carry_Underlying_Type_Except_Count_And_Avg()
    {
        var query = AqlParser.Parse(
            "FROM DataSet(\"AIRisk\") COLUMNS(category, MAX(severity), AVG(severity), COUNT(severity)) GROUP(category)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Equal(QueryDataType.Number, built.Projection[1].DataType); // MAX(severity) → number (source type)
        Assert.Equal(QueryDataType.Number, built.Projection[2].DataType); // AVG → always number
        Assert.Equal(QueryDataType.Number, built.Projection[3].DataType); // COUNT → always number
        Assert.Contains("MAX(\"severity\")", built.Command.CommandText);
        Assert.Contains("AVG(\"severity\")", built.Command.CommandText);
        Assert.Contains("COUNT(\"severity\")", built.Command.CommandText);
    }

    [Fact]
    public void OrderBy_Aggregate_Emits_Aggregate_Expression()
    {
        var query = AqlParser.Parse(
            "FROM DataSet(\"AIRisk\") ORDER BY COUNT() DESC COLUMNS(category, COUNT()) GROUP(category)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        Assert.Contains("ORDER BY COUNT(*) DESC", built.Command.CommandText);
    }

    [Fact]
    public void Where_With_Group_Composes_Where_Before_GroupBy()
    {
        var query = AqlParser.Parse(
            "FROM DataSet(\"AIRisk\") WHERE severity > 3 COLUMNS(category, COUNT()) GROUP(category)");

        var built = DatasetSqlBuilder.Build("ds_aiRisk", "rows", AiRiskSchema, query, hardCap: null);

        var sql = built.Command.CommandText;
        var whereIx = sql.IndexOf("WHERE", StringComparison.Ordinal);
        var groupIx = sql.IndexOf("GROUP BY", StringComparison.Ordinal);
        Assert.True(whereIx > 0 && groupIx > whereIx, $"Unexpected SQL: {sql}");
        Assert.Equal(1, built.ParameterCount);
    }
}
