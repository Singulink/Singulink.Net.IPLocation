namespace Singulink.Net.IPLocation.Tests;

[TestClass]
public class IPLocationResultTests
{
    [TestMethod]
    public void GetDisplayLocation_AllParts_JoinsThem()
    {
        var result = new IPLocationResult { IPAddress = "1.2.3.4", City = "Toronto", Region = "Ontario", CountryName = "Canada" };

        result.GetDisplayLocation().ShouldBe("Toronto, Ontario, Canada");
    }

    [TestMethod]
    public void GetDisplayLocation_RepeatedPart_IsLeftOut()
    {
        var result = new IPLocationResult { IPAddress = "1.2.3.4", City = "Singapore", Region = "Singapore", CountryName = "Singapore" };

        result.GetDisplayLocation().ShouldBe("Singapore");
    }

    [TestMethod]
    public void GetDisplayLocation_CountryOnly_ReturnsCountry()
    {
        var result = new IPLocationResult { IPAddress = "1.2.3.4", CountryName = "Canada" };

        result.GetDisplayLocation().ShouldBe("Canada");
    }

    [TestMethod]
    public void GetDisplayLocation_Private_ReturnsNull()
    {
        var result = new IPLocationResult { IPAddress = "10.0.0.1", IsPrivate = true };

        result.GetDisplayLocation().ShouldBeNull();
    }
}
