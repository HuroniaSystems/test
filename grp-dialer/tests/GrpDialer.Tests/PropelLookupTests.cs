using GrpDialer.Services;

namespace GrpDialer.Tests;

public class PropelLookupTests
{
    private const string Ns = @"xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://schemas.datacontract.org/2004/07/PFSP""";

    private static PropelPatient P(long id, string first, string last, string? phone) => new(id, last, first, phone);

    [Theory]
    [InlineData("svr1", "http://svr1/PFSP/HTML/api/v1")]
    [InlineData(" 192.168.1.5/ ", "http://192.168.1.5/PFSP/HTML/api/v1")]
    [InlineData("https://propel.local:8443/custom/api", "https://propel.local:8443/custom/api")]
    public void BaseUrl(string input, string expected) => Assert.Equal(expected, PropelLookup.BaseUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("ftp://svr1")]
    public void BaseUrlRejectsBad(string input) => Assert.Null(PropelLookup.BaseUrl(input));

    [Theory]
    [InlineData("7055551234", "7055551234")]
    [InlineData("17055551234", "7055551234")]
    [InlineData("+1 (705) 555-1234", "7055551234")]
    [InlineData("201", null)]
    [InlineData("5551234", null)]
    [InlineData("447700900123", null)]
    [InlineData(null, null)]
    public void SearchDigits(string? number, string? expected) => Assert.Equal(expected, PropelLookup.SearchDigits(number));

    [Fact]
    public void ParsesXml()
    {
        var xml = $@"<ArrayOfWfPatientProfileModel {Ns}><WfPatientProfileModel>
            <DateOfBirth>1968-08-29T00:00:00</DateOfBirth><Email i:nil=""true""/><FirstName>Cindy</FirstName>
            <Id>3576731</Id><LastName>Archer</LastName><Phone>7055299887</Phone></WfPatientProfileModel></ArrayOfWfPatientProfileModel>";
        Assert.Equal(P(3576731, "Cindy", "Archer", "7055299887"), Assert.Single(PropelLookup.ParsePatients(xml)));
    }

    [Fact]
    public void ParsesJsonAndEmpty()
    {
        var json = @"[{""Id"":12,""FirstName"":""Bo"",""LastName"":""Li"",""Phone"":null},{""Id"":0,""FirstName"":""x""}]";
        Assert.Equal(P(12, "Bo", "Li", null), Assert.Single(PropelLookup.ParsePatients(json)));
        Assert.Empty(PropelLookup.ParsePatients(""));
        Assert.Empty(PropelLookup.ParsePatients("null"));
        Assert.Empty(PropelLookup.ParsePatients($"<ArrayOfWfPatientProfileModel {Ns}/>"));
    }

    [Fact]
    public void RejectsNonPropelReplies() =>
        Assert.Throws<FormatException>(() => PropelLookup.ParsePatients("<html><body>IIS</body></html>"));

    [Fact]
    public void MatchingPrefersExactPhone()
    {
        var found = new List<PropelPatient> { P(1, "A", "One", "7055551234"), P(2, "B", "Two", "(705) 555-9999"), P(1, "A", "One", "7055551234") };
        Assert.Equal(new[] { 1L }, PropelLookup.Matching(found, "7055551234").Select(p => p.Id));
    }

    [Fact]
    public void MatchingTrustsShortListWithoutVisiblePhone()
    {
        var found = new List<PropelPatient> { P(1, "A", "One", null), P(2, "B", "Two", "7059990000") };
        Assert.Equal(2, PropelLookup.Matching(found, "7055551234").Count);
    }

    [Fact]
    public void MatchingDropsLongUnrelatedList()
    {
        var found = Enumerable.Range(1, 6).Select(i => P(i, "X", "Y", "4165550000")).ToList();
        Assert.Empty(PropelLookup.Matching(found, "7055551234"));
    }

    [Fact]
    public void Captions()
    {
        Assert.Null(PropelLookup.Caption(new List<PropelPatient>()));
        Assert.Equal("Potentially CINDY ARCHER", PropelLookup.Caption(new[] { P(1, "Cindy", "Archer", null) }));
        Assert.Equal("Potentially ARCHER", PropelLookup.Caption(new[] { P(1, "", "Archer", null) }));
        Assert.Equal("Multiple linked profiles", PropelLookup.Caption(new[] { P(1, "A", "B", null), P(2, "C", "D", null) }));
    }
}
