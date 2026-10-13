using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using eShop.Basket.UnitTests;
using graft.nuget.Ordering.API;
using OrderLine = graft.nuget.eShop.Ordering.API.OrderLine;
using OrderList = graft.nuget.eShop.Ordering.API.OrderList;
using OrderMutation = graft.nuget.eShop.Ordering.API.OrderMutation;
using OrdersApi = graft.nuget.eShop.Ordering.API.OrdersApi;

namespace eShop.Ordering.GraftTests;

[TestClass]
public class OrderingGatewayTests
{
    private const string Audience = "orders";
    private const string PostgresName = "eshop-ordering-graft-pg";
    private const int PostgresPort = 54329;

    public TestContext TestContext { get; set; } = null!;

    private static TokenAuthority authority = null!;
    private static Process gateway = null!;
    private static readonly StringBuilder gatewayOutput = new();

    [ClassInitialize]
    public static async Task StartGateway(TestContext _)
    {
        authority = await TokenAuthority.StartAsync();
        StartPostgres();
        var publish = PublishOrdering();
        CopySharedFramework(publish);
        var port = FreePort();
        var httpPort = FreePort();
        gateway = StartGatewayProcess(publish, port, httpPort);
        WaitForGateway();
        GraftConfig.Host = "ws://127.0.0.1:" + port + "/ws";
        GraftConfig.Stateless = true;
        try
        {
            WarmupGeneratedClient();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message + Environment.NewLine + SnapshotGateway(), ex);
        }
    }

    // The generated client registers OrderList, OrderHit, and OrderMutation in a static dictionary.
    // Do that once before any overlapping calls so concurrent A/B traffic does not race the registrar.
    private static void WarmupGeneratedClient()
    {
        var buyer = "warmup-" + Guid.NewGuid().ToString("N");
        var token = authority.Issue(buyer, Audience, audience: Audience);
        var empty = Call(token, () => OrdersApi.ListOrders());
        Assert.AreEqual("ok", empty.Status);
        Assert.AreEqual(0, empty.Count);

        var created = Create(token, 1);
        Assert.AreEqual("created", created.Status, created.Detail);
        Assert.AreNotEqual(0, created.OrderNumber, created.Detail);

        var page = Call(token, () => OrdersApi.ListOrders());
        Assert.AreEqual(1, page.Count);
        _ = page.OrderNumbers[0];
        _ = page.Dates[0];
        _ = page.Statuses[0];
        _ = page.Totals[0];

        var hit = Call(token, () => OrdersApi.GetOrder(created.OrderNumber));
        Assert.AreEqual(1, hit.Found);
        Assert.AreEqual("1 Microsoft Way", hit.Street);
        Assert.AreEqual(1, hit.ItemCount);
        Assert.AreEqual(1, hit.Items[0].ProductId);
        Assert.AreEqual("pic-1.png", hit.Items[0].PictureUrl);
        Assert.AreEqual(1, hit.Items[0].Units);
        Call(token, () => OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D")));
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

        RunDocker("rm -f " + PostgresName, ignoreFailure: true);
    }

    [TestMethod]
    public void UserSeesOnlyTheirOrdersAndCannotReadOrCancelAnother()
    {
        var tokenA = authority.Issue(NewBuyer(), Audience, audience: Audience);
        var tokenB = authority.Issue(NewBuyer(), Audience, audience: Audience);

        var createdA = Create(tokenA, 11);
        var createdB = Create(tokenB, 22);
        Assert.AreEqual("created", createdA.Status);
        Assert.AreEqual("created", createdB.Status);
        Assert.AreNotEqual(createdA.OrderNumber, createdB.OrderNumber);

        var mine = Call(tokenA, () => OrdersApi.ListOrders());
        Assert.AreEqual("ok", mine.Status);
        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual(createdA.OrderNumber, mine.OrderNumbers[0]);

        var theirs = Call(tokenB, () => OrdersApi.ListOrders());
        Assert.AreEqual(1, theirs.Count);
        Assert.AreEqual(createdB.OrderNumber, theirs.OrderNumbers[0]);

        var deniedRead = Call(tokenB, () => OrdersApi.GetOrder(createdA.OrderNumber));
        Assert.AreEqual("forbidden", deniedRead.Status);
        Assert.AreEqual(0, deniedRead.Found);

        var deniedCancel = Call(tokenB, () => OrdersApi.CancelOrder(createdA.OrderNumber, Guid.NewGuid().ToString("D")));
        Assert.AreEqual("forbidden", deniedCancel.Status);

        var stillMine = Call(tokenA, () => OrdersApi.GetOrder(createdA.OrderNumber));
        Assert.AreEqual("ok", stillMine.Status);
        Assert.AreEqual(1, stillMine.Found);
        Assert.AreEqual(createdA.OrderNumber, stillMine.OrderNumber);
        Assert.AreEqual("Submitted", stillMine.OrderStatus);
        Assert.AreEqual("Redmond", stillMine.City);
        Assert.AreEqual(1, stillMine.ItemCount);
        Assert.AreEqual(11, stillMine.Items[0].ProductId);
        Assert.AreEqual("pic-11.png", stillMine.Items[0].PictureUrl);
    }

    [TestMethod]
    public void MissingExpiredBadSignatureAndWrongScopeAreRejected()
    {
        var buyer = NewBuyer();
        var token = authority.Issue(buyer, Audience, audience: Audience);
        var created = Create(token, 5);
        Assert.AreEqual("created", created.Status);

        AssertRejected(OrdersApi.ListOrders());
        AssertRejected(CreateOrder(7));
        AssertRejected(OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D")));

        var expired = authority.Issue(buyer, Audience, DateTime.UtcNow.AddMinutes(-10), Audience);
        AssertRejected(Call(expired, () => OrdersApi.ListOrders()));
        AssertRejected(Create(expired, 8));
        AssertRejected(Call(expired, () => OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D"))));

        var badSignature = authority.Issue(buyer, Audience, audience: Audience, otherKey: true);
        AssertRejected(Call(badSignature, () => OrdersApi.ListOrders()));
        AssertRejected(Create(badSignature, 9));
        AssertRejected(Call(badSignature, () => OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D"))));

        var wrongAudience = authority.Issue(buyer, Audience, audience: "basket");
        AssertRejected(Call(wrongAudience, () => OrdersApi.ListOrders()));
        AssertRejected(Create(wrongAudience, 10));
        AssertRejected(Call(wrongAudience, () => OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D"))));

        var wrongScope = authority.Issue(buyer, "basket", audience: Audience);
        AssertRejected(Call(wrongScope, () => OrdersApi.ListOrders()));
        AssertRejected(Create(wrongScope, 12));
        AssertRejected(Call(wrongScope, () => OrdersApi.CancelOrder(created.OrderNumber, Guid.NewGuid().ToString("D"))));

        var stillThere = Call(token, () => OrdersApi.ListOrders());
        Assert.AreEqual("ok", stillThere.Status);
        Assert.AreEqual(1, stillThere.Count);
        Assert.AreEqual(created.OrderNumber, stillThere.OrderNumbers[0]);

        var unchanged = Call(token, () => OrdersApi.GetOrder(created.OrderNumber));
        Assert.AreEqual("Submitted", unchanged.OrderStatus);
    }

    [TestMethod]
    public void ConcurrentCallsDoNotMixIdentity()
    {
        for (var i = 0; i < 8; i++)
        {
            var tokenA = authority.Issue(NewBuyer(), Audience, audience: Audience);
            var tokenB = authority.Issue(NewBuyer(), Audience, audience: Audience);
            var gate = new Barrier(2);
            OrderMutation createdA = null!;
            OrderMutation createdB = null!;
            OrderList listedA = null!;
            OrderList listedB = null!;
            var cancellation = TestContext.CancellationToken;
            var first = Task.Run(() =>
            {
                gate.SignalAndWait(cancellation);
                createdA = Create(tokenA, 1000 + i);
                listedA = Call(tokenA, () => OrdersApi.ListOrders());
            }, cancellation);
            var second = Task.Run(() =>
            {
                gate.SignalAndWait(cancellation);
                createdB = Create(tokenB, 2000 + i);
                listedB = Call(tokenB, () => OrdersApi.ListOrders());
            }, cancellation);
            Task.WaitAll([first, second], TestContext.CancellationToken);
            Assert.AreEqual("created", createdA.Status);
            Assert.AreEqual("created", createdB.Status);
            Assert.AreNotEqual(createdA.OrderNumber, createdB.OrderNumber);
            Assert.AreEqual("ok", listedA.Status);
            Assert.AreEqual(1, listedA.Count);
            Assert.AreEqual(createdA.OrderNumber, listedA.OrderNumbers[0]);
            Assert.AreEqual("ok", listedB.Status);
            Assert.AreEqual(1, listedB.Count);
            Assert.AreEqual(createdB.OrderNumber, listedB.OrderNumbers[0]);
        }
    }

    private static void AssertRejected(OrderList result)
    {
        Assert.AreEqual("unauthenticated", result.Status);
        Assert.AreEqual(0, result.Count);
    }

    private static void AssertRejected(OrderMutation result)
    {
        Assert.AreEqual("unauthenticated", result.Status);
        Assert.AreEqual(0, result.OrderNumber);
    }

    private static OrderMutation Create(string token, int productId) =>
        Call(token, () => CreateOrder(productId));

    private static OrderMutation CreateOrder(int productId) =>
        OrdersApi.CreateOrder(
            Guid.NewGuid().ToString("D"),
            "TESTUSER",
            "Redmond",
            "1 Microsoft Way",
            "WA",
            "USA",
            "98052",
            "1111222233334444",
            "TESTUSER",
            DateTime.UtcNow.AddYears(1).ToString("o"),
            "111",
            1,
            [new OrderLine(productId, "Product", 1, 10d, "pic-" + productId + ".png")]);

    private static T Call<T>(string token, Func<T> action)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token
        };
        return GraftConfig.InvokeWithHeaders(action, headers);
    }

    private static string NewBuyer() => "buyer-" + Guid.NewGuid().ToString("N");

    private static void StartPostgres()
    {
        RunDocker("rm -f " + PostgresName, ignoreFailure: true);
        var started = RunDocker(
            "run -d --name " + PostgresName +
            " -e POSTGRES_PASSWORD=postgres -e POSTGRES_USER=postgres -e POSTGRES_DB=orderingdb" +
            " -p " + PostgresPort + ":5432 postgres:16-alpine");
        if (started != 0)
        {
            throw new InvalidOperationException("docker run postgres failed.");
        }

        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (RunDocker("exec " + PostgresName + " pg_isready -U postgres -d orderingdb", ignoreFailure: true) == 0)
            {
                return;
            }

            Thread.Sleep(500);
        }

        throw new TimeoutException("Timed out waiting for postgres.");
    }

    private static int RunDocker(string arguments, bool ignoreFailure = false)
    {
        var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("docker did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException("docker " + arguments + " failed: " + stderr + stdout);
        }

        return process.ExitCode;
    }

    private static string PublishOrdering()
    {
        var root = FindRepoRoot();
        var publish = Path.Combine(Path.GetTempPath(), "eshop-ordering-graft-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "src", "Ordering.API", "Ordering.API.csproj");
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
        var module = Path.Combine(publish, "Ordering.API.dll");
        var start = new ProcessStartInfo(
            "gg",
            "--runtime netcore --modules \"" + module + "\" --types eShop.Ordering.API.OrdersApi,eShop.Ordering.API.OrderList,eShop.Ordering.API.OrderHit,eShop.Ordering.API.OrderMutation,eShop.Ordering.API.OrderLine --port " + port + " --httpPort " + httpPort)
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
        start.Environment["ConnectionStrings__orderingdb"] = "Host=127.0.0.1;Port=" + PostgresPort + ";Database=orderingdb;Username=postgres;Password=postgres";
        start.Environment["Identity__Url"] = authority.Issuer;
        start.Environment["Identity__Audience"] = Audience;
        start.Environment.Remove("ESHOP_GRAFT_TRANSPORT");
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
            throw new TimeoutException("Timed out waiting for the ordering gateway. " + gatewayOutput);
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

    private static string SnapshotGateway()
    {
        lock (gatewayOutput)
        {
            return gatewayOutput.ToString();
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
