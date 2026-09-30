# eShop Reference Application - "AdventureWorks"

A reference .NET application implementing an e-commerce website using a services-based architecture with [Aspire](https://aspire.dev/).

![eShop Reference Application architecture diagram](img/eshop_architecture.png)

![eShop homepage screenshot](img/eshop_homepage.png)

This fork keeps the AdventureWorks services. Catalog reads and basket updates from the Blazor storefront are in-place Graftcode facades: the original `CatalogApi` and `BasketService` methods, called through Graftcode Gateway instead of catalog REST and basket gRPC. Order integration that used to publish on the Aspire RabbitMQ event bus (order started, stock, payment, price, webhooks) is the same style of graft call over [RabbitmqPlugin](https://github.com/grft-dev/graftcode-plugins/tree/main/rabbitmq). Moving that path to Service Bus later is a plugin name and config change, not a new call site. The storefront orders page polls, because a Gateway process does not own the Blazor circuit. See [Catalog and basket on Graftcode](#catalog-and-basket-on-graftcode).

## Getting Started

This version of eShop is based on .NET 10.

Previous eShop versions:

* [.NET 8](https://github.com/dotnet/eShop/tree/release/8.0)

### Prerequisites

1. Install a [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) that satisfies [`global.json`](global.json).
2. Install the [Aspire CLI](https://aspire.dev/get-started/install-cli/) and verify that it is available:

    ```console
    aspire --version
    ```

3. Install and start an OCI-compatible container runtime. [Docker Desktop](https://www.docker.com/products/docker-desktop/) is the recommended default. [Podman](https://podman.io/docs/installation) is also supported; follow the [Aspire prerequisites](https://aspire.dev/get-started/prerequisites/) to configure it.
4. Clone the repository:

    ```console
    git clone https://github.com/dotnet/eShop.git
    cd eShop
    ```

No separate Aspire workload or Visual Studio component is required; the AppHost SDK and hosting integrations are referenced by the projects in this repository.

#### Optional IDE setup

- [Visual Studio](https://visualstudio.microsoft.com/vs/) with the `ASP.NET and web development` workload.
- [Visual Studio Code with C# Dev Kit](https://code.visualstudio.com/docs/csharp/get-started) and the [Aspire extension](https://aspire.dev/get-started/aspire-vscode-extension/).
- The [.NET MAUI workload](https://learn.microsoft.com/dotnet/maui/get-started/installation) if you want to run the client apps.

### Running the solution

> [!WARNING]
> Ensure that your container runtime is running before starting eShop.

#### From the terminal

From the repository root, run:

```console
aspire run
```

The root [`aspire.config.json`](aspire.config.json) selects `src/eShop.AppHost/eShop.AppHost.csproj`, avoiding ambiguity with the test AppHosts in the repository. When startup completes, the CLI prints a dashboard URL similar to:

```text
Dashboard: https://localhost:<port>/login?t=<token>
```

Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop the AppHost. See the [`aspire run` command](https://aspire.dev/reference/cli/commands/aspire-run/) for additional options.

To run the AppHost in the background instead:

```console
aspire start
aspire ps
```

When you are finished, run `aspire stop`. See the [`aspire start` command](https://aspire.dev/reference/cli/commands/aspire-start/) for details.

#### From Visual Studio

1. Open `eShop.Web.slnf`.
2. Set `src/eShop.AppHost/eShop.AppHost.csproj` as the startup project.
3. Press <kbd>Ctrl</kbd>+<kbd>F5</kbd> to start eShop and open the Aspire dashboard.

### Running tests

Run the server tests:

```powershell
dotnet test --solution eShop.Web.slnf
```

### Optional: AI Chatbot with Microsoft Foundry

This option provisions a Microsoft Foundry resource during local development, so first authenticate to Azure and configure the subscription and location:

```powershell
az login
aspire secret set "Azure:SubscriptionId" "<subscription-id>"
aspire secret set "Azure:Location" "eastus"
```

Then enable Foundry and start eShop:

```powershell
$env:UseFoundry = "true"
aspire run
```

Aspire provisions the `gpt-4.1-mini` and `text-embedding-3-small` deployments and injects their connection information into the consuming projects. The Foundry hosting integration currently uses a preview package. See [local Azure provisioning](https://aspire.dev/integrations/cloud/azure/local-provisioning/) and the [Microsoft Foundry hosting integration](https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-host/) for details.

### Deploy to Azure Container Apps

The AppHost is already configured with an Azure Container Apps environment, so the Aspire CLI can deploy directly from the application model. See the [Aspire Azure Container Apps deployment guide](https://aspire.dev/deployment/azure/container-apps/) for details.

> [!WARNING]
> This sample deploys PostgreSQL, Redis, and RabbitMQ as containers in Azure Container Apps. This configuration is intended for evaluation and demonstrations, not production data.

Prerequisites:

- The prerequisites listed above, including a running container runtime.
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), an active Azure subscription, and permission to create resources.

Sign in, optionally preview the deployment pipeline, and deploy:

```console
az login
aspire deploy --list-steps
aspire deploy
```

For local interactive use, `aspire deploy` prompts for missing Azure settings. For non-interactive use, provide them explicitly:

```powershell
$env:Azure__SubscriptionId = "<subscription-id>"
$env:Azure__Location = "eastus"
$env:Azure__ResourceGroup = "rg-eshop-demo"
aspire deploy --non-interactive
```

Use [`aspire publish`](https://aspire.dev/reference/cli/commands/aspire-publish/) when you need deployment artifacts for inspection or another deployment tool. Running it first is not required: `aspire deploy` invokes the deployment pipeline and its dependencies directly rather than consuming an earlier publish output.

When you no longer need the deployment, run [`aspire destroy`](https://aspire.dev/reference/cli/commands/aspire-destroy/). This deletes the entire configured resource group, including resources that Aspire did not create, so review the target carefully before confirming.

## Catalog and basket on Graftcode

Catalog business logic stayed in `src/Catalog.API/Apis/CatalogApi.cs`. The Minimal API handlers are now public static methods on that class (`GetItem`, `ListItems`, `GetItemsByIds`, `Search`, `ListBrands`, `ListTypes`, `GetFacets`, `CreateItem`, `UpdateItem`, `DeleteItem`, `GetItemPicture`). There is no second graft type and no `MapCatalogApi` route table. `CatalogService` / `ICatalogService` are unchanged as types. The storefront calls those public methods over Graftcode Gateway and deserializes the JSON strings. Return values are strings or primitives, not `object`.

Basket followed the same pattern, in the original file `src/Basket.API/Grpc/BasketService.cs`. That class still talks to the Redis repository. `GetBasket`, `UpdateBasket`, `DeleteBasket`, and `OnOrderStarted` are public static methods (buyer id and a JSON item list) instead of gRPC overrides. The type stays `eShop.Basket.API.BasketService`, which is what `gg --types` already hosts. There is no `Basket.BasketBase`. The web app type is still `BasketService`. It no longer uses `GrpcBasketClient`. `MapGrpcService` and `src/Basket.API/Proto/basket.proto` are gone. The MAUI client keeps its own proto copy and is outside this slice.

Product images: browsers need a URL, so the web app maps `GET /product-images/{id}` and returns the bytes from `CatalogApi.GetItemPicture` (JSON with a MIME type and base64). That is the only leftover HTTP for catalog pictures. The mobile BFF no longer proxies `/api/catalog/...`. The MAUI catalog client still speaks those old REST paths and will not hit this gateway until it calls the same public methods.

`aspire run` still starts `catalog-api`, `basket-api`, `ordering-api`, `order-processor`, `payment-processor`, and `webhooks-api`. Those processes migrate data and keep their HTTP APIs. They do not subscribe to the Aspire event bus. Cross-service calls go through Gateway on RabbitmqPlugin when `ESHOP_GRAFT_TRANSPORT=rabbitmq` (the AppHost sets this on ordering, catalog, and the order processor). A standalone Gateway process sets `EshopGraftHost` so it does not also turn on storefront authentication. A price change calls `ProductPriceChangedIntegrationEventHandler.OnProductPriceChanged` on that same transport.

### In-place REST and gRPC → Graft

`GetCatalogItem` no longer builds `api/catalog/items/{id}`:

```csharp
// before — WebAppComponents/Services/CatalogService.cs
var uri = $"{remoteServiceBaseUrl}items/{id}";
return httpClient.GetFromJsonAsync<CatalogItem>(uri);

// after — same CatalogService, generated graft called like a local method
return Task.FromResult(Parse<CatalogItem>(CatalogApi.GetItem(id)));
```

The EF query stayed in `CatalogApi`:

```csharp
public static string ListItems(int pageIndex, int pageSize, string name, string typeIds, string brandIds) =>
    Block(async services => ToJson(await ListItemsAsync(
        services, pageIndex, pageSize, EmptyToNull(name), ParseIds(typeIds), ParseIds(brandIds))));
```

Basket updates no longer build a protobuf request:

```csharp
// before — WebApp/Services/BasketService.cs
await basketClient.UpdateBasketAsync(updatePayload);

// after — same BasketService, generated graft
BasketApi.UpdateBasket(await BuyerIdAsync(), payload);
```

### Why Graftcode

Counted non-blank, non-comment lines against `main`. The storefront calls generated Graft packages (`CatalogApi.GetItem`, `BasketApi.UpdateBasket`). It does not open a socket or invoke methods by name.

| Piece | Before | After |
| --- | ---: | ---: |
| `MapCatalogApi` route table | 95 | 0 |
| `CatalogApi.cs` | 389 | 419 |
| `CatalogService` | 71 | 90 |
| `Catalog.API` `Program.cs` | 15 | 7 |
| gRPC `Basket.API/Grpc/BasketService.cs` | 91 | 179 |
| Web app `BasketService` | 40 | 92 |
| `basket.proto` | 24 | 0 |

`CatalogApi.cs` grew from 389 to 419 non-blank lines. The route table was replaced by public method wrappers, and order-stock handlers are now `OnOrderAwaitingValidation` and `OnOrderPaid` on the same class. The EF queries stayed in that file. `CatalogService` is 90 lines (71 on `main`). The web app `BasketService` is 92 lines (40 on `main`). Those clients only set `GraftConfig` and parse JSON. A handwritten gateway client was larger (123 and 120); the generated grafts replaced it. `CatalogApi.cs` plus `CatalogService` went from 460 to 509 non-blank lines. `src/Basket.API/Grpc/BasketService.cs` is 179 non-blank lines, including `OnOrderStarted`. The hosted type is still `eShop.Basket.API.BasketService`.

Generated OpenAPI documents (`Catalog.API.json`, 1260 lines, and `Catalog.API_v2.json`, 1043 lines) and `Catalog.API.http` left with the route table. They were generated contracts, not the query logic.

HTTP functional tests (`CatalogApiTests`, 345 non-blank lines, `WebApplicationFactory` and `/api/catalog/...`) are replaced by `CatalogMethodTests` (189), which call the public methods on the same Postgres test host. Basket unit tests call `GetBasket`, `UpdateBasket`, and `DeleteBasket` in memory against a mock repository.

### WebSocket gateway (default)

Install the Graftcode skill (it is not committed in this repo):

```powershell
# Windows
iwr grft.dev/get | iex
```

```bash
# Unix
curl -fsSL grft.dev/get | sh
```

Install Graftcode Gateway:

```bash
curl -fsSL grft.dev/get/gg | sh
```

Catalog needs Postgres with pgvector (the image Aspire uses). Basket needs Redis. One `gg` process can host both assemblies on port 8000.

```bash
docker run -d --name eshop-catalog-pg \
  -e POSTGRES_PASSWORD=Pass@word \
  -e POSTGRES_DB=catalogdb \
  -p 5432:5432 ankane/pgvector
docker run -d --name eshop-basket-redis -p 6379:6379 redis

export ConnectionStrings__catalogdb="Host=localhost;Port=5432;Database=catalogdb;Username=postgres;Password=Pass@word"
export ConnectionStrings__redis="localhost:6379"

dotnet publish src/Catalog.API/Catalog.API.csproj -c Release -o ./artifacts/catalog-graft
dotnet publish src/Basket.API/Basket.API.csproj -c Release -o ./artifacts/basket-graft

./gg --runtime netcore \
  --modules ./artifacts/catalog-graft/Catalog.API.dll,./artifacts/basket-graft/Basket.API.dll \
  --types eShop.Catalog.API.CatalogApi,eShop.Basket.API.BasketService \
  --port 8000 \
  --corsAllowedOrigins "http://localhost:5045,https://localhost:7298"
```

- Graftcode Vision: http://localhost:8000
- WebSocket: `ws://localhost:8000/ws`

The first catalog call migrates and seeds `Setup/catalog.json`. The AppHost sets `CATALOG_GRAFT_HOST` and `BASKET_GRAFT_HOST` to `ws://localhost:8000/ws` on the web app. Start the gateway before opening the storefront.

### Install the grafts

With `gg` running, open Graftcode Vision at http://localhost:8000, choose NuGet, and copy the install command. The packages checked in for this slice were produced that way:

```bash
dotnet add package graft.nuget.catalog.api_063hpr -v 1.0.0 --source https://grft.dev/45702aba-f4ff-47e5-9157-bf8096d331aa__free
dotnet add package graft.nuget.basket.api_4lefa6 -v 1.0.0 --source https://grft.dev/1d322126-f16f-4b21-9e9f-454bff8f240e__free
dotnet add package graft.nuget.ordering.api_kmowfv -v 1.0.0 --source https://grft.dev/6bdbab5c-e6f2-47ed-99c2-2e43ef4749ce__free
dotnet add package graft.nuget.paymentprocessor_2hchbx -v 1.0.0 --source https://grft.dev/fe8171ea-ebec-4496-860e-a0a48111bd04__free
dotnet add package graft.nuget.webhooks.api_tu0l8p -v 1.0.0 --source https://grft.dev/fb4ccfe5-1a92-4aba-a688-fc33b25f196d__free
```

`nuget.config` also restores those same nupkgs from `graft/feed`, so `dotnet build` does not need the registry to be up. If you host the modules again, Vision prints a new command. Replace the `PackageReference` and the nupkg in `graft/feed` with that package. The package id suffix comes from the gateway; it is not a hand-written name.

The generated types are `graft.nuget.eShop.Catalog.API.CatalogApi` and `graft.nuget.eShop.Basket.API.BasketService`. `CatalogService` and the web app `BasketService` set `GraftConfig.Host` (default `ws://localhost:8000/ws`) and call those methods. Basket's DI constructor is private so the graft only contains the static methods. A public constructor of `IBasketRepository` and `ILogger` makes the NuGet graft fail to build.

`CatalogService` and `BasketService` run inside the Blazor Server process, so the storefront does not need browser CORS for these calls. Pass `--corsAllowedOrigins` when a browser client calls the gateway from another origin (the web app launch profile is `http://localhost:5045` and `https://localhost:7298`).

The same public methods are what an MCP client calls. Copy the MCP client configuration from the Graftcode Vision portal. Vision is the module graph, not the MCP endpoint.

### RabbitMQ plugin

[RabbitmqPlugin](https://github.com/grft-dev/graftcode-plugins/tree/main/rabbitmq) carries the same method calls over RabbitMQ request/reply instead of the websocket. Do not commit the plugin binary. Build it from that repo (`cmake -S . -B build && cmake --build build --config Release`). The output is `RabbitmqPlugin.dll` or `libRabbitmqPlugin.dll`. If the file has the `lib` prefix, set `"name"` to `libRabbitmqPlugin`. Put the DLL where `gg` loads plugins (next to the `gg` binary). Gateway releases are at [grft-dev/graftcode-gateway](https://github.com/grft-dev/graftcode-gateway/releases).

Sample configs (guest/guest on localhost:5672):

- `graft/pluginConfig.catalog.rabbitmq.json` — queues `eshop.catalog` and `eshop.catalog.reply`
- `graft/pluginConfig.basket.rabbitmq.json` — queues `eshop.basket` and `eshop.basket.reply`

Declare those queues before starting the gateway (RabbitMQ management UI, or `rabbitmqadmin declare queue`). One plugin config has one queue pair, so catalog and basket are two `gg` processes:

```bash
./gg ./artifacts/catalog-graft/Catalog.API.dll --config graft/pluginConfig.catalog.rabbitmq.json
./gg ./artifacts/basket-graft/Basket.API.dll --config graft/pluginConfig.basket.rabbitmq.json
```

Point the web app at that transport (default remains websocket; this is opt-in):

```bash
export CATALOG_GRAFT_TRANSPORT=rabbitmq
export BASKET_GRAFT_TRANSPORT=rabbitmq
export CATALOG_GRAFT_PLUGIN_HOST=localhost:5672
export BASKET_GRAFT_PLUGIN_HOST=localhost:5672
# optional; these are GraftConfig.SetConfig documents (configurations.plugin), not the gg --config file
export CATALOG_GRAFT_PLUGIN_CONFIG=$PWD/graft/graftConfig.catalog.json
export BASKET_GRAFT_PLUGIN_CONFIG=$PWD/graft/graftConfig.basket.json
```

Aspire still starts the RabbitMQ container named `eventbus`. Nothing in Catalog, Basket, Ordering, OrderProcessor, PaymentProcessor, WebApp, or Webhooks calls `AddRabbitMqEventBus` anymore. The container is the broker for RabbitmqPlugin. Copy its published host port, user, and password from the Aspire dashboard into the sample JSON and into `ESHOP_GRAFT_PLUGIN_HOST` (`host:port`). Aspire does not publish guest/guest on port 5672 unless you set that yourself. The AppHost default is `localhost:5672`.

### Integration events as graft calls

Each old subscription is a public method on the service that already owned the handler. Callers use the generated graft as a local method. `GraftConfig.SetConfig` sends the plugin block (the same shape as `graft/graftConfig.*.json`). One plugin config is one queue pair, so each module is its own `gg` process.

| Call | Queue pair | What it replaces |
| --- | --- | --- |
| `BasketService.OnOrderStarted` | `eshop.basket` / `eshop.basket.reply` | Basket subscription to `OrderStarted` |
| `CatalogApi.OnOrderAwaitingValidation` | `eshop.catalog` / `eshop.catalog.reply` | Catalog subscription to awaiting-validation. Returns `confirmed` or `rejected` |
| `CatalogApi.OnOrderPaid` | same catalog queues | Catalog subscription to paid |
| `GracePeriodConfirmedIntegrationEventHandler.OnGracePeriodConfirmed` | `eshop.ordering` / `eshop.ordering.reply` | Ordering subscription to grace period. Called by the order processor |
| `OrderStatusChangedToStockConfirmedIntegrationEventHandler.OnStockConfirmed` | `eshop.payment` / `eshop.payment.reply` | Payment subscription to stock confirmed. Returns `succeeded` or `failed` |
| `OrderStatusChangedToPaidIntegrationEventHandler.OnOrderPaid`, `OrderStatusChangedToShippedIntegrationEventHandler.OnOrderShipped`, `ProductPriceChangedIntegrationEventHandler.OnProductPriceChanged` | `eshop.webhooks` / `eshop.webhooks.reply` | Webhooks subscriptions, including the price-changed handler |

Ordering applies the catalog and payment return values in-process (`OrderStockConfirmedIntegrationEventHandler.Apply`, `OrderPaymentSucceededIntegrationEventHandler.Apply`, and the other handler `Apply` methods). Those methods are also hosted (`OnStockConfirmed`, `OnStockRejected`, `OnPaymentSucceeded`, `OnPaymentFailed`) but the live path does not RPC back into the ordering queue. The ordering call is still on the stack, and one RabbitmqPlugin consumer would deadlock on a nested call to itself.

The storefront orders page (`OrdersRefreshOnStatusChange.razor`) polls every 5 seconds. The old handlers only called `OrderStatusNotificationService` inside the Blazor process. A graft hosted by `gg` runs in a different process and cannot refresh that circuit, so WebApp has no queue and no `AddRabbitMqEventBus`.

`Ordering.API` calls the basket, catalog, payment, and webhooks grafts. `Catalog.API` calls the webhooks graft for a price change. `OrderProcessor` calls `GracePeriodConfirmedIntegrationEventHandler.OnGracePeriodConfirmed`. Example:

```csharp
CatalogGraft.OnOrderAwaitingValidation(orderId, stockItemsJson);
PaymentGraft.OnStockConfirmed(orderId);
OrderingGraft.OnGracePeriodConfirmed(orderId);
```

Run the integration gateways (declare each queue pair first). Point them at the same RabbitMQ Aspire published, and pass the database connection strings those modules already use:

```bash
export ESHOP_GRAFT_TRANSPORT=rabbitmq
export ESHOP_GRAFT_PLUGIN_HOST=localhost:5672
export ESHOP_GRAFT_PLUGIN_USER=guest
export ESHOP_GRAFT_PLUGIN_PASSWORD=guest
# Later Service Bus: change the plugin name (and its fields). Call sites stay as they are.
# export ESHOP_GRAFT_PLUGIN_NAME=ServiceBusPlugin

export ConnectionStrings__catalogdb="Host=localhost;Port=5432;Database=catalogdb;Username=postgres;Password=..."
export ConnectionStrings__redis="localhost:6379"
export ConnectionStrings__orderingdb="Host=localhost;Port=5432;Database=orderingdb;Username=postgres;Password=..."
export ConnectionStrings__webhooksdb="Host=localhost;Port=5432;Database=webhooksdb;Username=postgres;Password=..."

./gg /path/to/Catalog.API.dll --config graft/pluginConfig.catalog.rabbitmq.json --types eShop.Catalog.API.CatalogApi
./gg /path/to/Basket.API.dll --config graft/pluginConfig.basket.rabbitmq.json --types eShop.Basket.API.BasketService
./gg /path/to/Ordering.API.dll --config graft/pluginConfig.ordering.rabbitmq.json --types eShop.Ordering.API.Application.IntegrationEvents.EventHandling.GracePeriodConfirmedIntegrationEventHandler,eShop.Ordering.API.Application.IntegrationEvents.EventHandling.OrderStockConfirmedIntegrationEventHandler,eShop.Ordering.API.Application.IntegrationEvents.EventHandling.OrderStockRejectedIntegrationEventHandler,eShop.Ordering.API.Application.IntegrationEvents.EventHandling.OrderPaymentSucceededIntegrationEventHandler,eShop.Ordering.API.Application.IntegrationEvents.EventHandling.OrderPaymentFailedIntegrationEventHandler
./gg /path/to/PaymentProcessor.dll --config graft/pluginConfig.payment.rabbitmq.json --types eShop.PaymentProcessor.IntegrationEvents.EventHandling.OrderStatusChangedToStockConfirmedIntegrationEventHandler
./gg /path/to/Webhooks.API.dll --config graft/pluginConfig.webhooks.rabbitmq.json --types Webhooks.API.IntegrationEvents.OrderStatusChangedToPaidIntegrationEventHandler,Webhooks.API.IntegrationEvents.OrderStatusChangedToShippedIntegrationEventHandler,Webhooks.API.IntegrationEvents.ProductPriceChangedIntegrationEventHandler
```

The storefront websocket gateway on port 8000 can still host catalog and basket for reads. Integration calls use the RabbitmqPlugin processes above, even when the browser path stays on `ws://localhost:8000/ws`. If `ESHOP_GRAFT_TRANSPORT` is not `rabbitmq`, ordering logs and skips the remote call so unit tests do not need a broker. The AppHost sets the variable. Without the five `gg` processes and a matching `ESHOP_GRAFT_PLUGIN_HOST`, order stock, payment, basket cleanup, and webhooks do not run.

Server configs are `graft/pluginConfig.*.rabbitmq.json` (`gg --config`). Client samples are `graft/graftConfig.*.json` (`GraftConfig.SetConfig`). Do not pass a server file to `SetConfig`. The integration processes build that client JSON from the environment variables above. `ESHOP_GRAFT_PLUGIN_NAME` defaults to `RabbitmqPlugin`. If the built library is `libRabbitmqPlugin.dll`, set the name to `libRabbitmqPlugin`.

`EventBus` still holds `IntegrationEvent` and `IIntegrationEventHandler` for the outbox. `EventBusRabbitMQ` is gone. No service registers a bus.

## Contributing

For more information on contributing to this repo, read [the contribution documentation](./CONTRIBUTING.md) and [the Code of Conduct](CODE-OF-CONDUCT.md).

### Sample data

The sample catalog data is defined in [catalog.json](https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Setup/catalog.json). Those product names, descriptions, and brand names are fictional and were generated using [GPT-35-Turbo](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/chatgpt), and the corresponding [product images](https://github.com/dotnet/eShop/tree/main/src/Catalog.API/Pics) were generated using [DALL·E 3](https://openai.com/dall-e-3).

## eShop on Azure

For a version of this app configured for deployment on Azure, please view [the eShop on Azure](https://github.com/Azure-Samples/eShopOnAzure) repo.
