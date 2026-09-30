# eShop Reference Application - "AdventureWorks"

A reference .NET application implementing an e-commerce website using a services-based architecture with [Aspire](https://aspire.dev/).

![eShop Reference Application architecture diagram](img/eshop_architecture.png)

![eShop homepage screenshot](img/eshop_homepage.png)

This fork keeps the AdventureWorks services. The catalog slice the Blazor storefront reads (`Catalog.API` and `WebAppComponents` `CatalogService`) is a thin Graftcode facade: the same EF queries, called as public methods on `CatalogGraft` instead of REST URLs. Basket, ordering, identity, and payment are unchanged. See [Catalog on Graftcode](#catalog-on-graftcode).

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

## Catalog on Graftcode

Legacy HTTP routes in `CatalogApi.MapCatalogApi` are still mapped, so functional tests, OpenAPI, and the product-image proxy keep working. Each handler is a thin wrapper. The query and command bodies live on `CatalogGraft`, and the public methods are what Graftcode Gateway hosts. `CatalogService` is still `CatalogService` / `ICatalogService`. Its HTTP client calls are graft calls. `CATALOG_GRAFT_HOST` defaults to `ws://localhost:8000/ws`. The AppHost sets that variable on the web app.

### In-place REST → Graft

`GetCatalogItem` no longer builds `api/catalog/items/{id}`:

```csharp
// before — WebAppComponents/Services/CatalogService.cs
var uri = $"{remoteServiceBaseUrl}items/{id}";
return httpClient.GetFromJsonAsync<CatalogItem>(uri);

// after — same type, CatalogGraft.GetItem over the gateway
return Task.FromResult(Parse<CatalogItem>(Call("GetItem", id)));
```

`GetAllItems` no longer owns the EF query. It returns the page `CatalogGraft` already computed:

```csharp
var page = await CatalogGraft.ListItemsAsync(
    services,
    paginationRequest.PageIndex,
    paginationRequest.PageSize,
    name,
    type,
    brand);
return TypedResults.Ok(page);
```

The storefront calls `ListItems`, `GetItem`, `GetItemsByIds`, `Search`, `ListBrands`, `ListTypes`, and `GetFacets`. Those methods return JSON strings. `CatalogService` deserializes them into the existing catalog records. Generic `object` results are not part of the surface. Create, update, delete, and picture bytes are on the same class for the gateway. The picture the browser shows is still the legacy route `GET /api/catalog/items/{id}/pic`, forwarded by the web app as `/product-images/{id}`.

### Why Graftcode

Counted non-blank, non-comment lines on the path the storefront actually used.

| Piece | Before | After |
| --- | ---: | ---: |
| `MapCatalogApi` route table | 94 | not called by `CatalogService` (the 94 lines are still in the file for legacy HTTP) |
| Public read methods on `CatalogGraft` | 0 | 22 |
| `CatalogService` | 71 | 74 |
| **Storefront path** | **165** | **96** |

That path is **42%** smaller (165 → 96, (165 − 96) / 165). The client stopped assembling query strings and API versions, and the methods it calls are the contract.

The rest of the diff is not a reduction, and it should not be folded into that percentage:

- `CatalogApi.cs` went from 389 to 262 non-blank lines. The EF bodies moved; they were not deleted. Those cores are 186 non-blank lines inside `CatalogGraft`.
- Gateway host glue (process startup, JSON helpers, picture bytes) is 121 non-blank lines. `CatalogApi` + `CatalogGraft` + `CatalogService` together are 714 non-blank lines, up from 460 (**+55%**). The HTTP wrappers stayed so the existing catalog tests and the image proxy still compile and run.

### Run the catalog gateway

Install the Graftcode skill (it is not committed in this repo):

```powershell
# Windows
iwr grft.dev/get | iex
```

```bash
# Unix
curl -fsSL grft.dev/get | sh
```

Install Graftcode Gateway, then a Postgres image that includes pgvector (the same image Aspire uses):

```bash
curl -fsSL grft.dev/get/gg | sh

docker run -d --name eshop-catalog-pg \
  -e POSTGRES_PASSWORD=Pass@word \
  -e POSTGRES_DB=catalogdb \
  -p 5432:5432 ankane/pgvector

export ConnectionStrings__catalogdb="Host=localhost;Port=5432;Database=catalogdb;Username=postgres;Password=Pass@word"
```

Publish the existing catalog project and host `CatalogGraft`. The first call migrates and seeds `Setup/catalog.json`.

```bash
dotnet publish src/Catalog.API/Catalog.API.csproj -c Release -o ./artifacts/catalog-graft

./gg --runtime netcore \
  --modules ./artifacts/catalog-graft/Catalog.API.dll \
  --types eShop.Catalog.API.CatalogGraft \
  --port 8000 \
  --corsAllowedOrigins "http://localhost:5045,https://localhost:7298"
```

- Graftcode Vision: http://localhost:8000
- WebSocket: `ws://localhost:8000/ws`

`CatalogService` runs inside the Blazor Server process, so the storefront does not need browser CORS for catalog reads. Pass `--corsAllowedOrigins` when a browser client calls the gateway from another origin (the web app's launch profile is `http://localhost:5045` and `https://localhost:7298`; add the Aspire dashboard origin if you open the store from there).

`aspire run` still starts the rest of the stack, including `catalog-api` for the picture proxy and for anything that was not part of this slice. Start the gateway before opening the storefront. Catalog list, search, brands, types, and facets come from `CatalogGraft`. Basket, ordering, identity, and payment are unchanged.

The same public methods are MCP-ready: static methods, primitive arguments, string results. Copy the MCP client configuration from the Graftcode Vision portal. That portal is where the configuration is shown. Vision is the module graph, not the MCP endpoint.

## Contributing

For more information on contributing to this repo, read [the contribution documentation](./CONTRIBUTING.md) and [the Code of Conduct](CODE-OF-CONDUCT.md).

### Sample data

The sample catalog data is defined in [catalog.json](https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Setup/catalog.json). Those product names, descriptions, and brand names are fictional and were generated using [GPT-35-Turbo](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/chatgpt), and the corresponding [product images](https://github.com/dotnet/eShop/tree/main/src/Catalog.API/Pics) were generated using [DALL·E 3](https://openai.com/dall-e-3).

## eShop on Azure

For a version of this app configured for deployment on Azure, please view [the eShop on Azure](https://github.com/Azure-Samples/eShopOnAzure) repo.
