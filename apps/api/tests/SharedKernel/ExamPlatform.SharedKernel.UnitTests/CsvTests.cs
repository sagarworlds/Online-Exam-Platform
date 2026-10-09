using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.UnitTests;

public class CsvTests
{
    [Fact]
    public void Parse_SplitsPlainFieldsByComma()
    {
        var rows = Csv.Parse("a,b,c\n1,2,3\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void Parse_WithNoTrailingNewline_StillClosesTheLastRow()
    {
        var rows = Csv.Parse("a,b\n1,2");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void Parse_HandlesQuotedFieldsContainingCommas()
    {
        var rows = Csv.Parse("\"Smith, John\",42\n");

        Assert.Equal(["Smith, John", "42"], rows[0]);
    }

    [Fact]
    public void Parse_HandlesDoubledQuotesInsideAQuotedField()
    {
        var rows = Csv.Parse("\"She said \"\"hi\"\"\",ok\n");

        Assert.Equal(["She said \"hi\"", "ok"], rows[0]);
    }

    [Fact]
    public void Parse_HandlesNewlinesInsideAQuotedField()
    {
        var rows = Csv.Parse("\"line one\nline two\",b\n");

        Assert.Single(rows);
        Assert.Equal(["line one\nline two", "b"], rows[0]);
    }

    [Fact]
    public void Parse_HandlesWindowsLineEndings_AsOneRowBreak()
    {
        var rows = Csv.Parse("a,b\r\n1,2\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void Parse_OfAnEmptyField_ProducesAnEmptyString()
    {
        var rows = Csv.Parse("a,,c\n");

        Assert.Equal(["a", "", "c"], rows[0]);
    }

    [Fact]
    public void Parse_SkipsABlankLine_AsARowOfOneEmptyField()
    {
        var rows = Csv.Parse("a,b\n\nc,d\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal([""], rows[1]);
    }

    [Fact]
    public void WriteRow_LeavesAPlainFieldUnquoted()
    {
        Assert.Equal("a,b,c\r\n", Csv.WriteRow(["a", "b", "c"]));
    }

    [Fact]
    public void WriteRow_QuotesAFieldContainingAComma()
    {
        Assert.Equal("\"a,b\",c\r\n", Csv.WriteRow(["a,b", "c"]));
    }

    [Fact]
    public void WriteRow_DoublesAQuoteInsideAQuotedField()
    {
        Assert.Equal("\"she said \"\"hi\"\"\"\r\n", Csv.WriteRow(["she said \"hi\""]));
    }

    [Fact]
    public void WriteRow_QuotesAFieldContainingANewline()
    {
        Assert.Equal("\"line one\nline two\"\r\n", Csv.WriteRow(["line one\nline two"]));
    }

    [Fact]
    public void WriteRow_TreatsANullFieldAsEmpty()
    {
        Assert.Equal("a,,c\r\n", Csv.WriteRow(["a", null, "c"]));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("with, a comma")]
    [InlineData("with \"quotes\"")]
    [InlineData("with\na newline")]
    [InlineData("")]
    public void WritingThenParsing_RoundTripsTheFieldExactly(string field)
    {
        var csv = Csv.WriteRow([field, "other"]);

        var rows = Csv.Parse(csv);

        Assert.Equal(field, rows[0][0]);
    }
}
