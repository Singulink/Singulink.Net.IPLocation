namespace Singulink.Net.IPLocation.Tests;

[TestClass]
public class AddressScopeTests
{
    [TestMethod]
    [DataRow("10.1.2.3")]
    [DataRow("172.16.0.1")]
    [DataRow("172.31.255.255")]
    [DataRow("192.168.1.1")]
    [DataRow("127.0.0.1")]
    [DataRow("169.254.10.20")]
    [DataRow("100.64.0.1")]
    [DataRow("0.0.0.0")]
    [DataRow("255.255.255.255")]
    [DataRow("224.0.0.251")]
    [DataRow("203.0.113.5")]
    [DataRow("::1")]
    [DataRow("::")]
    [DataRow("fe80::1")]
    [DataRow("fd12:3456:789a::1")]
    [DataRow("2001:db8::1")]
    [DataRow("ff02::1")]
    public void IsPrivate_NonPublicAddress_ReturnsTrue(string address)
    {
        AddressScope.IsPrivate(AddressScope.Normalize(IPAddress.Parse(address))).ShouldBeTrue();
    }

    [TestMethod]
    [DataRow("8.8.8.8")]
    [DataRow("1.1.1.1")]
    [DataRow("172.32.0.1")]
    [DataRow("100.128.0.1")]
    [DataRow("2607:f8b0:4005:80f::200e")]
    [DataRow("::ffff:8.8.8.8")]
    public void IsPrivate_PublicAddress_ReturnsFalse(string address)
    {
        AddressScope.IsPrivate(AddressScope.Normalize(IPAddress.Parse(address))).ShouldBeFalse();
    }

    [TestMethod]
    public void Normalize_MappedIPv4_ReturnsIPv4()
    {
        AddressScope.Normalize(IPAddress.Parse("::ffff:10.0.0.1")).ToString().ShouldBe("10.0.0.1");
    }

    [TestMethod]
    public void Normalize_ScopeId_IsRemoved()
    {
        AddressScope.Normalize(IPAddress.Parse("fe80::1%12")).ToString().ShouldBe("fe80::1");
    }
}
