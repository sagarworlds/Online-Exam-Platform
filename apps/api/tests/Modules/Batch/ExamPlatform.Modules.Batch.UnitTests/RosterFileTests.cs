using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Domain.Exceptions;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// Reading a roster file (FR-50): an optional header, the e-mail and phone columns found by name or by position, and
/// row numbers that match the file the person opens again to fix a row.
/// </summary>
public class RosterFileTests
{
    [Fact]
    public void Read_WithAHeader_FindsTheColumnsByName_InAnyOrder()
    {
        var file = RosterFile.Read("Phone,Email\r\n+919876543210,amy@example.com\r\n");

        var row = Assert.Single(file.Rows);
        Assert.Equal("amy@example.com", row.Email);
        Assert.Equal("+919876543210", row.Phone);
        Assert.Equal(2, row.Row);
        Assert.Empty(file.IgnoredColumns);
    }

    [Theory]
    [InlineData("E-mail address")]
    [InlineData("e_mail")]
    [InlineData("  EMAIL  ")]
    [InlineData("Email ID")]
    public void Read_RecognisesTheUsualNamesForTheEmailColumn(string name)
    {
        var file = RosterFile.Read($"{name}\r\namy@example.com\r\n");

        Assert.Equal("amy@example.com", Assert.Single(file.Rows).Email);
    }

    [Theory]
    [InlineData("Mobile")]
    [InlineData("Phone number")]
    public void Read_RecognisesTheUsualNamesForThePhoneColumn(string name)
    {
        var file = RosterFile.Read($"email,{name}\r\namy@example.com,+919876543210\r\n");

        Assert.Equal("+919876543210", Assert.Single(file.Rows).Phone);
    }

    [Fact]
    public void Read_WithoutAHeader_TakesTheFirstColumnAsTheAddress_AndTheSecondAsThePhone_AndKeepsTheFirstRow()
    {
        var file = RosterFile.Read("amy@example.com,+919876543210\r\nben@example.com\r\n");

        Assert.Equal(["amy@example.com", "ben@example.com"], file.Rows.Select(r => r.Email));
        Assert.Equal(["+919876543210", null], file.Rows.Select(r => r.Phone));
        Assert.Equal([1, 2], file.Rows.Select(r => r.Row));
    }

    [Fact]
    public void Read_WithAHeaderOfOnlyAPhoneColumn_IsRefused_NotGuessedAt()
    {
        var error = Assert.Throws<RosterUnreadableError>(() => RosterFile.Read("Phone\r\n+919876543210\r\n"));

        Assert.Contains("e-mail column", error.Message);
    }

    [Fact]
    public void Read_ReportsTheColumnsItDoesNotUse_ByName()
    {
        var file = RosterFile.Read("Name,Email,Roll number\r\nAmy,amy@example.com,17\r\n");

        Assert.Equal(["Name", "Roll number"], file.IgnoredColumns);
        Assert.Equal("amy@example.com", Assert.Single(file.Rows).Email);
    }

    [Fact]
    public void Read_WithoutAHeader_ReportsColumnsPastTheSecond_ByPosition()
    {
        var file = RosterFile.Read("amy@example.com,+919876543210,Amy,17\r\n");

        Assert.Equal(["Column 3", "Column 4"], file.IgnoredColumns);
    }

    [Fact]
    public void Read_ShortensAVeryLongIgnoredHeader()
    {
        var file = RosterFile.Read($"{new string('n', 500)},email\r\namy,amy@example.com\r\n");

        Assert.True(Assert.Single(file.IgnoredColumns).Length < 60);
    }

    [Fact]
    public void Read_SkipsBlankLines_ButKeepsRowNumbersTrueToTheFile()
    {
        var file = RosterFile.Read("email\r\namy@example.com\r\n\r\nben@example.com\r\n");

        Assert.Equal([2, 4], file.Rows.Select(r => r.Row));
    }

    [Fact]
    public void Read_FindsTheHeaderAfterLeadingBlankLines()
    {
        var file = RosterFile.Read("\r\n\r\nemail\r\namy@example.com\r\n");

        var row = Assert.Single(file.Rows);
        Assert.Equal("amy@example.com", row.Email);
        Assert.Equal(4, row.Row);
    }

    [Fact]
    public void Read_ARowWithNoAddress_IsKept_SoTheBatchCanReportIt()
    {
        var file = RosterFile.Read("email,phone\r\n,+919876543210\r\n");

        var row = Assert.Single(file.Rows);
        Assert.Null(row.Email);
        Assert.Equal("+919876543210", row.Phone);
    }

    [Fact]
    public void Read_ARowOfOnlyEmptyCells_IsABlankLine_AsASpreadsheetExportsThem()
    {
        var file = RosterFile.Read("email,phone\r\n,\r\namy@example.com,\r\n");

        var row = Assert.Single(file.Rows);
        Assert.Equal(3, row.Row);
    }

    [Fact]
    public void Read_ABlankPhone_IsNoPhone()
    {
        var file = RosterFile.Read("email,phone\r\namy@example.com,   \r\n");

        Assert.Null(Assert.Single(file.Rows).Phone);
    }

    [Fact]
    public void Read_IgnoresAByteOrderMark_SoTheFirstAddressStaysValid()
    {
        var file = RosterFile.Read("﻿email\r\namy@example.com\r\n");

        Assert.Equal("amy@example.com", Assert.Single(file.Rows).Email);
    }

    [Fact]
    public void Read_ABomBeforeAHeaderlessFirstAddress_IsDropped()
    {
        var file = RosterFile.Read("﻿amy@example.com\r\n");

        Assert.Equal("amy@example.com", Assert.Single(file.Rows).Email);
    }

    [Fact]
    public void Read_HandlesQuotedFields_AndALastRowWithoutANewline()
    {
        var file = RosterFile.Read("\"Name, with comma\",email\r\n\"Amy\",amy@example.com");

        Assert.Equal("amy@example.com", Assert.Single(file.Rows).Email);
        Assert.Equal(["Name, with comma"], file.IgnoredColumns);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\r\n  ")]
    public void Read_WithNothingInTheFile_IsRefused(string? content)
    {
        var error = Assert.Throws<RosterUnreadableError>(() => RosterFile.Read(content));

        Assert.Contains("empty", error.Message);
    }

    [Fact]
    public void Read_WithOnlyAHeader_IsRefused()
    {
        var error = Assert.Throws<RosterUnreadableError>(() => RosterFile.Read("email,phone\r\n"));

        Assert.Contains("no rows", error.Message);
    }
}
