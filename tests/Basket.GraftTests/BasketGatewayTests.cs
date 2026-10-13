using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using eShop.Basket.UnitTests;
using graft.nuget.Basket.API;
using BasketApi = graft.nuget.eShop.Basket.API.BasketService;
using BasketResult = graft.nuget.eShop.Basket.API.BasketResult;

namespace eShop.Basket.GraftTests;

[TestClass]
public class BasketGatewayTests
{
    public TestContext TestContext { get; set; } = null!;

    private static TokenAuthority authority = null!;
    private static Process gateway = null!;
    private static readonly StringBuilder gatewayOutput = new();

    [ClassInitialize]
    public static async Task StartGateway(TestContext _)
    {
        authority = await TokenAuthority.StartAsync();
        var publish = PublishBasket();
        CopySharedFramework(publish);
        var port = FreePort();
        var httpPort = FreePort();
        gateway = StartGatewayProcess(publish, port, httpPort);
        WaitForGateway();
        GraftConfig.Host = "ws://127.0.0.1:" + port + "/ws";
        GraftConfig.Stateless = true;
        WarmupGeneratedClient();
    }

    // The generated client registers BasketResult in a static dictionary. Do that once
    // before any overlapping calls so concurrent A/B traffic does not race the registrar.
    private static void WarmupGeneratedClient()
    {
        var buyer = "warmup-" + Guid.NewGuid().ToString("N");
        Call(authority.Issue(buyer, "basket"), () => BasketApi.GetBasket());
        Call(authority.Issue(buyer, "basket"), () => BasketApi.UpdateBasket([1], [1]));
        Call(authority.Issue(buyer, "basket"), () => BasketApi.DeleteBasket());
        Call(authority.Issue("ordering", "basket.internal"), () => BasketApi.OnOrderStarted(buyer));
    }

    [ClassCleanup]
    public static async Task StopGateway()
    {
        if (gateway is not null && !gateway.HasExited)
        {
            gateway.Kill(entireProcessTree: true);
            gateway.WaitForExit(5000);
        }

        if (authority is not null)
        {
            await authority.DisposeAsync();
        }
    }

    [TestMethod]
    public void UserReadsAndUpdatesOnlyTheirBasket()
    {
        var buyerA = NewBuyer();
        var buyerB = NewBuyer();
        var tokenA = authority.Issue(buyerA, "basket");
        var tokenB = authority.Issue(buyerB, "basket");

        var updated = Call(tokenA, () => BasketApi.UpdateBasket([42], [3]));
        Assert.AreEqual("ok", updated.Status);
        Assert.AreEqual(1, updated.Count);
        Assert.AreEqual(42, updated.ProductIds[0]);
        Assert.AreEqual(3, updated.Quantities[0]);

        Call(tokenB, () => BasketApi.UpdateBasket([99], [1]));

        var mine = Call(tokenA, () => BasketApi.GetBasket());
        Assert.AreEqual("ok", mine.Status);
        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual(42, mine.ProductIds[0]);

        var theirs = Call(tokenB, () => BasketApi.GetBasket());
        Assert.AreEqual(1, theirs.Count);
        Assert.AreEqual(99, theirs.ProductIds[0]);
    }

    [TestMethod]
    public void AnonymousReadDoesNotReturnAnotherUsersBasket()
    {
        var buyer = NewBuyer();
        var token = authority.Issue(buyer, "basket");
        Call(token, () => BasketApi.UpdateBasket([7], [1]));

        var anon = BasketApi.GetBasket();
        Assert.AreEqual("ok", anon.Status);
        Assert.AreEqual(0, anon.Count);
    }

    [TestMethod]
    public void MissingExpiredAndInvalidTokensCannotWriteOrDelete()
    {
        var buyer = NewBuyer();
        Call(authority.Issue(buyer, "basket"), () => BasketApi.UpdateBasket([5], [1]));

        AssertRejected(BasketApi.UpdateBasket([8], [1]));
        AssertRejected(BasketApi.DeleteBasket());
        AssertRejected(Call(authority.Issue(buyer, "basket", DateTime.UtcNow.AddMinutes(-10)), () => BasketApi.UpdateBasket([8], [1])));
        AssertRejected(Call(authority.Issue(buyer, "basket", otherKey: true), () => BasketApi.DeleteBasket()));
        AssertRejected(Call(authority.Issue(buyer, "basket", audience: "orders"), () => BasketApi.UpdateBasket([8], [1])));
        AssertRejected(Call(authority.Issue(buyer, "orders"), () => BasketApi.DeleteBasket()));

        var stillThere = Call(authority.Issue(buyer, "basket"), () => BasketApi.GetBasket());
        Assert.AreEqual(1, stillThere.Count);
        Assert.AreEqual(5, stillThere.ProductIds[0]);
    }

    [TestMethod]
    public void UserTokenCannotInvokeOrderingOperation()
    {
        var buyerA = NewBuyer();
        var buyerB = NewBuyer();
        var user = authority.Issue(buyerA, "basket");
        var service = authority.Issue("ordering", "basket.internal");
        Call(user, () => BasketApi.UpdateBasket([4], [2]));
        Call(authority.Issue(buyerB, "basket"), () => BasketApi.UpdateBasket([6], [1]));

        var denied = Call(user, () => BasketApi.OnOrderStarted(buyerB));
        Assert.AreEqual("forbidden", denied.Status);

        var other = Call(authority.Issue(buyerB, "basket"), () => BasketApi.GetBasket());
        Assert.AreEqual(1, other.Count);
        Assert.AreEqual(6, other.ProductIds[0]);

        var deleted = Call(service, () => BasketApi.OnOrderStarted(buyerA));
        Assert.AreEqual("deleted", deleted.Status);

        var mine = Call(user, () => BasketApi.GetBasket());
        Assert.AreEqual(0, mine.Count);
        var otherAfter = Call(authority.Issue(buyerB, "basket"), () => BasketApi.GetBasket());
        Assert.AreEqual(1, otherAfter.Count);
        Assert.AreEqual(6, otherAfter.ProductIds[0]);
    }

    [TestMethod]
    public void ConcurrentCallsDoNotMixIdentity()
    {
        for (var i = 0; i < 8; i++)
        {
            var buyerA = NewBuyer();
            var buyerB = NewBuyer();
            var tokenA = authority.Issue(buyerA, "basket");
            var tokenB = authority.Issue(buyerB, "basket");
            var gate = new Barrier(2);
            BasketResult resultA = null!;
            BasketResult resultB = null!;
            var cancellation = TestContext.CancellationToken;
            var first = Task.Run(() =>
            {
                gate.SignalAndWait(cancellation);
                Call(tokenA, () => BasketApi.UpdateBasket([1000 + i], [1]));
                resultA = Call(tokenA, () => BasketApi.GetBasket());
            }, cancellation);
            var second = Task.Run(() =>
            {
                gate.SignalAndWait(cancellation);
                Call(tokenB, () => BasketApi.UpdateBasket([2000 + i], [2]));
                resultB = Call(tokenB, () => BasketApi.GetBasket());
            }, cancellation);
            Task.WaitAll([first, second], TestContext.CancellationToken);
            Assert.AreEqual("ok", resultA.Status);
            Assert.AreEqual(1000 + i, resultA.ProductIds[0]);
            Assert.AreEqual("ok", resultB.Status);
            Assert.AreEqual(2000 + i, resultB.ProductIds[0]);
        }
    }

    private static void AssertRejected(BasketResult result)
    {
        Assert.AreEqual("unauthenticated", result.Status);
        Assert.AreEqual(0, result.Count);
    }

    private static BasketResult Call(string token, Func<BasketResult> action)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token
        };
        return GraftConfig.InvokeWithHeaders(action, headers);
    }

    private static string NewBuyer() => "buyer-" + Guid.NewGuid().ToString("N");

    private static string PublishBasket()
    {
        var root = FindRepoRoot();
        var publish = Path.Combine(Path.GetTempPath(), "eshop-basket-graft-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "src", "Basket.API", "Basket.API.csproj");
        var publishProcess = Process.Start(new ProcessStartInfo("dotnet", "publish \"" + project + "\" -c Release -o \"" + publish + "\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("dotnet publish did not start.");
        var stdout = publishProcess.StandardOutput.ReadToEnd();
        var stderr = publishProcess.StandardError.ReadToEnd();
        publishProcess.WaitForExit();
        if (publishProcess.ExitCode != 0)
        {
            throw new InvalidOperationException("dotnet publish failed: " + stderr + stdout);
        }

        return publish;
    }

    private static void CopySharedFramework(string publish)
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        }

        var shared = Path.Combine(dotnetRoot, "shared", "Microsoft.AspNetCore.App");
        if (!Directory.Exists(shared))
        {
            return;
        }

        var version = Directory.GetDirectories(shared).OrderByDescending(Path.GetFileName).First();
        foreach (var file in Directory.GetFiles(version, "*.dll"))
        {
            var dest = Path.Combine(publish, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                File.Copy(file, dest);
            }
        }
    }

    private static Process StartGatewayProcess(string publish, int port, int httpPort)
    {
        var module = Path.Combine(publish, "Basket.API.dll");
        var start = new ProcessStartInfo(
            "gg",
            "--runtime netcore --modules \"" + module + "\" --types eShop.Basket.API.BasketService,eShop.Basket.API.BasketResult --port " + port + " --httpPort " + httpPort)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        }

        start.Environment["DOTNET_ROOT"] = dotnetRoot;
        start.Environment["PATH"] = dotnetRoot + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["ConnectionStrings__redis"] = "127.0.0.1:6379";
        start.Environment["Identity__Url"] = authority.Issuer;
        start.Environment["Identity__Audience"] = TokenAuthority.Audience;
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");

        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, eventArgs) => AppendGateway(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendGateway(eventArgs.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("gg did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void WaitForGateway()
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < until)
        {
            lock (gatewayOutput)
            {
                var text = gatewayOutput.ToString();
                if (text.Contains("Websocket server is available", StringComparison.Ordinal))
                {
                    return;
                }

                if (gateway.HasExited)
                {
                    throw new InvalidOperationException("gg exited before the websocket server started. " + text);
                }
            }

            Thread.Sleep(200);
        }

        lock (gatewayOutput)
        {
            throw new TimeoutException("Timed out waiting for the basket gateway. " + gatewayOutput);
        }
    }

    private static void AppendGateway(string line)
    {
        if (line is null)
        {
            return;
        }

        lock (gatewayOutput)
        {
            gatewayOutput.AppendLine(line);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "eShop.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory);
    }
}
