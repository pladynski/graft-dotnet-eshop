using eShop.ClientApp.Services;

namespace ClientApp.UnitTests.Services;

[TestClass]
public class GraftHostsTests
{
    [TestMethod]
    public void EmptySettingUsesTheDefaultSocket()
    {
        var socket = GraftHosts.Socket("  ");

        Assert.EndsWith(":8000/ws", socket);
        Assert.StartsWith("ws://", socket);
    }

    [TestMethod]
    public void WebSocketSettingKeepsItsHostAndPort()
    {
        Assert.AreEqual("ws://10.0.2.2:8000/ws", GraftHosts.Socket("ws://10.0.2.2:8000"));
        Assert.AreEqual("wss://gateway.example:9443/ws", GraftHosts.Socket("wss://gateway.example:9443/ws"));
    }

    [TestMethod]
    public void HttpSettingBecomesTheSameHostWebSocket()
    {
        Assert.AreEqual("ws://10.0.2.2:11632/ws", GraftHosts.Socket("http://10.0.2.2:11632/"));
        Assert.AreEqual("wss://shop.example/ws", GraftHosts.Socket("https://shop.example"));
    }
}
