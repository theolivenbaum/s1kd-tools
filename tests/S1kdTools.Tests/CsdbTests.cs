using S1kdTools;

namespace S1kdTools.Tests;

public class CsdbTests
{
    [Theory]
    [InlineData("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML", true)]
    [InlineData("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.xml", true)] // .XML case-insensitive
    [InlineData("PMC-EX-12345-00001-00_001-00_EN-CA.XML", false)]
    [InlineData("DMC-EX.txt", false)]
    public void IsDataModule_DetectsDmc(string name, bool expected) =>
        Assert.Equal(expected, Csdb.IsDataModule(name));

    [Theory]
    [InlineData("PMC-EX-12345-00001-00_001-00_EN-CA.XML", true)]
    [InlineData("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML", false)]
    public void IsPublicationModule_DetectsPmc(string name, bool expected) =>
        Assert.Equal(expected, Csdb.IsPublicationModule(name));

    [Theory]
    [InlineData("ICN-EX-A-000000-A-00001-A-001-01.PNG", true)]
    [InlineData("SMC-EX-A-00.XML", false)]
    public void IsIcn_DetectsIcn(string name, bool expected) =>
        Assert.Equal(expected, Csdb.IsIcn(name));

    [Theory]
    [InlineData("5", "1~10", true)]
    [InlineData("30", "20~100", true)]      // numeric, not lexicographic
    [InlineData("100", "20~100", true)]
    [InlineData("101", "20~100", false)]
    [InlineData("b", "a~c", true)]
    [InlineData("d", "a~c", false)]
    [InlineData("7", "7", true)]
    [InlineData("8", "7", false)]
    public void IsInRange_Works(string value, string range, bool expected) =>
        Assert.Equal(expected, Csdb.IsInRange(value, range));

    [Theory]
    [InlineData("2", "1|2|3", true)]
    [InlineData("5", "1|2|3", false)]
    [InlineData("25", "1|20~30|99", true)]  // range inside a set
    [InlineData("50", "1|20~30|99", false)]
    public void IsInSet_Works(string value, string set, bool expected) =>
        Assert.Equal(expected, Csdb.IsInSet(value, set));

    [Theory]
    [InlineData("DMC-", "DMC-EX-A.XML", true)]
    [InlineData("DM?-", "DMC-EX-A.XML", true)]  // ? wildcard
    [InlineData("PMC-", "DMC-EX-A.XML", false)]
    public void StrMatch_Works(string pattern, string value, bool expected) =>
        Assert.Equal(expected, Csdb.StrMatch(pattern, value));

    [Fact]
    public void ExtractLatestObjects_KeepsHighestIssuePerCode()
    {
        var files = new[]
        {
            "DMC-EX-A-00_001-00_EN-CA.XML",
            "DMC-EX-A-00_001-01_EN-CA.XML",
            "DMC-EX-A-00_002-00_EN-CA.XML",
            "DMC-EX-B-00_001-00_EN-CA.XML",
        };
        var latest = Csdb.ExtractLatestObjects(files);
        Assert.Equal(new[]
        {
            "DMC-EX-A-00_002-00_EN-CA.XML",
            "DMC-EX-B-00_001-00_EN-CA.XML",
        }, latest);
    }
}
