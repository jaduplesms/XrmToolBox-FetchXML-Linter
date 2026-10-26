using System;
using FetchXmlLinter.Core;
using Xunit;

namespace FetchXmlLinter.Tests;

public class ShortlistTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string User = "22222222-2222-2222-2222-222222222222";
    private static string Row(string userType = "DataverseSystemUserId", string view = "", string attribution = "UserTableCandidate") =>
        "1," + Organization + "," + User + "," + userType + "," + view + "," + attribution + ",account,,,,,";

    [Fact]
    public void UserOnlyCandidateDoesNotBecomeAnExactView()
    {
        var shortlist = ShortlistCsv.Read(ShortlistCsv.Header + "\r\n" + Row(), Organization);
        Assert.Empty(shortlist.Errors);
        Assert.Null(Assert.Single(shortlist.Candidates).ViewId);
    }

    [Fact]
    public void RejectsCrossOrganizationInput()
    {
        var shortlist = ShortlistCsv.Read(ShortlistCsv.Header + "\n" + Row(), Guid.NewGuid());
        Assert.Empty(shortlist.Candidates);
        Assert.Contains(shortlist.Errors, s => s.Contains("organization"));
    }

    [Theory]
    [InlineData("Unknown", "", "UserTableCandidate")]
    [InlineData("EntraObjectId", "", "ExactViewMarker")]
    [InlineData("EntraObjectId", "33333333-3333-3333-3333-333333333333", "UserTableCandidate")]
    public void RejectsInvalidIdentityOrAttribution(string type, string view, string attribution)
    {
        var shortlist = ShortlistCsv.Read(ShortlistCsv.Header + "\n" + Row(type, view, attribution), Organization);
        Assert.Empty(shortlist.Candidates);
        Assert.NotEmpty(shortlist.Errors);
    }

    [Fact]
    public void CountsDuplicatesAndRetainsQuotedCommas()
    {
        var row = Row().Replace(",account,", ",\"account,synthetic\",");
        var shortlist = ShortlistCsv.Read(ShortlistCsv.Header + "\n" + row + "\n" + row, Organization);
        Assert.Empty(shortlist.Errors);
        Assert.Equal(1, shortlist.DuplicateCount);
        Assert.Equal("account,synthetic", Assert.Single(shortlist.Candidates).EntityName);
    }

    [Fact]
    public void InvalidRowsRemainVisibleAlongsideValidRows()
    {
        var shortlist = ShortlistCsv.Read(ShortlistCsv.Header + "\n" + Row() + "\nbad,row", Organization);
        Assert.Single(shortlist.Candidates);
        Assert.Single(shortlist.Errors);
    }

    [Fact]
    public void RejectsUnsafeOrAmbiguousCsv()
    {
        Assert.NotEmpty(ShortlistCsv.Read(ShortlistCsv.Header + "\n\"unclosed", Organization).Errors);
        Assert.NotEmpty(ShortlistCsv.Read(ShortlistCsv.Header + ",UserId\n" + Row(), Organization).Errors);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]
    [InlineData(" \t+SUM(1)")]
    [InlineData("@command")]
    public void ExportNeutralizesSpreadsheetFormulas(string value)
    {
        Assert.StartsWith("\"'", ShortlistCsv.EncodeCell(value));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "20", "120.5", "PageLoad", true)]
    [InlineData("2026-01-02T00:00:00Z", "2026-01-01T00:00:00Z", "20", "120.5", "PageLoad", false)]
    [InlineData("2026-01-01T00:00:00+01:00", "2026-01-02T00:00:00Z", "20", "120.5", "PageLoad", false)]
    [InlineData("January 1 2026Z", "2026-01-02T00:00:00Z", "20", "120.5", "PageLoad", false)]
    [InlineData("2026-01-01T00:00:00.123Z", "2026-01-02T00:00:00.1234567Z", "20", "120.5", "PageLoad", true)]
    [InlineData("", "", "20", "120.5", "ServerRequest", false)]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "0", "120.5", "PageLoad", false)]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "20", "NaN", "PageLoad", false)]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "20", "-1", "PageLoad", false)]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "20", "10", "SqlDuration", false)]
    public void ValidatesObservedTimingProvenance(string start, string end, string count, string p95, string metric, bool valid)
    {
        var row = "1," + Organization + "," + User + ",EntraObjectId,,UserTableCandidate,account," +
            string.Join(",", start, end, count, p95, metric);
        var result = ShortlistCsv.Read(ShortlistCsv.Header + "\n" + row, Organization);
        Assert.Equal(valid, result.Errors.Count == 0);
        Assert.Equal(valid ? 1 : 0, result.Candidates.Count);
    }
}
