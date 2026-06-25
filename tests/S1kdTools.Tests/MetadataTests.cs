using S1kdTools;

namespace S1kdTools.Tests;

public class MetadataTests
{
    [Theory]
    [InlineData("type", "dmodule")]
    [InlineData("techName", "Example")]
    [InlineData("infoName", "Description")]
    [InlineData("issueNumber", "002")]
    [InlineData("inWork", "01")]
    [InlineData("issueInfo", "002-01")]
    [InlineData("securityClassification", "01")]
    [InlineData("issueType", "changed")]
    [InlineData("language", "en-CA")]
    [InlineData("modelIdentCode", "EX")]
    [InlineData("infoCode", "040")]
    [InlineData("responsiblePartnerCompany", "Example Company")]
    [InlineData("originatorCode", "ABCDE")]
    public void Get_ReturnsExpected(string key, string expected)
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Equal(expected, Metadata.Get(doc, key));
    }

    [Fact]
    public void Get_MissingMetadata_ReturnsNull()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.Null(Metadata.Get(doc, "pmTitle"));
    }

    [Fact]
    public void Set_TechName_Updates()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "techName", "Changed Name"));
        Assert.Equal("Changed Name", Metadata.Get(doc, "techName"));
    }

    [Fact]
    public void Set_IssueNumber_UpdatesAttribute()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "issueNumber", "003"));
        Assert.Equal("003", Metadata.Get(doc, "issueNumber"));
        Assert.Equal("003-01", Metadata.Get(doc, "issueInfo"));
    }

    [Fact]
    public void Set_SecurityClassification_UpdatesAttribute()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.True(Metadata.Set(doc, "securityClassification", "02"));
        Assert.Equal("02", Metadata.Get(doc, "securityClassification"));
    }

    [Fact]
    public void Set_NonEditableKey_ReturnsFalse()
    {
        var doc = XmlUtils.ReadMem(Fixtures.DataModule);
        Assert.False(Metadata.Set(doc, "type", "whatever"));
    }

    [Fact]
    public void AllKeys_HaveUniqueNames()
    {
        var dupes = Metadata.Keys.GroupBy(k => k.Name).Where(g => g.Count() > 1).ToList();
        Assert.Empty(dupes);
    }
}
