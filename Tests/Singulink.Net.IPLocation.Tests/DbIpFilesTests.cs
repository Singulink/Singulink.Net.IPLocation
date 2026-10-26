namespace Singulink.Net.IPLocation.Tests;

[TestClass]
public class DbIpFilesTests
{
    private static readonly DateOnly October2026 = new(2026, 10, 1);

    [TestMethod]
    public void GetFileName_ReturnsDbIpName()
    {
        DbIpFiles.GetFileName(DbIpDatabaseKind.City, October2026).ShouldBe("dbip-city-lite-2026-10.mmdb");
        DbIpFiles.GetFileName(DbIpDatabaseKind.Asn, October2026).ShouldBe("dbip-asn-lite-2026-10.mmdb");
    }

    [TestMethod]
    public void GetDownloadUri_ReturnsCompressedFileUri()
    {
        var uri = DbIpFiles.GetDownloadUri(new Uri("https://download.db-ip.com/free/"), DbIpDatabaseKind.City, October2026);

        uri.ToString().ShouldBe("https://download.db-ip.com/free/dbip-city-lite-2026-10.mmdb.gz");
    }

    [TestMethod]
    public void GetMonth_FileOfKind_ReturnsMonth()
    {
        DbIpFiles.GetMonth(DbIpDatabaseKind.City, "dbip-city-lite-2026-10.mmdb").ShouldBe(October2026);
    }

    [TestMethod]
    [DataRow("dbip-asn-lite-2026-10.mmdb")]
    [DataRow("dbip-city-lite-2026-10.mmdb.download")]
    [DataRow("dbip-city-lite-2026-13.mmdb")]
    [DataRow("dbip-city-lite-latest.mmdb")]
    public void GetMonth_OtherFile_ReturnsNull(string fileName)
    {
        DbIpFiles.GetMonth(DbIpDatabaseKind.City, fileName).ShouldBeNull();
    }

    [TestMethod]
    public void GetMonth_Time_ReturnsFirstOfMonth()
    {
        DbIpFiles.GetMonth(new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc)).ShouldBe(October2026);
    }
}
