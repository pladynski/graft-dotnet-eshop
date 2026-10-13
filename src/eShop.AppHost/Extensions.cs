// Graftcode catalog slice — mobile BFF no longer proxies catalog HTTP.
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Foundry;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace eShop.AppHost;

internal static class Extensions
{
    public static bool IsFoundryEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration["UseFoundry"], out var useFoundry) && useFoundry;

    /// <summary>
    /// Adds a hook to set the ASPNETCORE_FORWARDEDHEADERS_ENABLED environment variable to true for all projects in the application.
    /// </summary>
    public static IDistributedApplicationBuilder AddForwardedHeaders(this IDistributedApplicationBuilder builder)
    {
        builder.Services.TryAddEventingSubscriber<AddForwardHeadersSubscriber>();
        return builder;
    }

    private class AddForwardHeadersSubscriber : IDistributedApplicationEventingSubscriber
    {
        public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
        {
            eventing.Subscribe<BeforeStartEvent>((@event, ct) =>
            {
                foreach (var p in @event.Model.GetProjectResources())
                {
                    p.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                    {
                        context.EnvironmentVariables["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
                    }));
                }

                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Configures eShop projects to use Microsoft Foundry for text embedding and chat.
    /// </summary>
    public static IDistributedApplicationBuilder AddFoundry(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> catalogApi,
        IResourceBuilder<ProjectResource> webApp)
    {
        var foundry = builder.AddFoundry("foundry");
        var chat = foundry.AddDeployment("chatModel", "gpt-4.1-mini", "2025-04-14", "OpenAI");
        var textEmbedding = foundry.AddDeployment("textEmbeddingModel", "text-embedding-3-small", "1", "OpenAI");

        catalogApi.WithReference(textEmbedding).WaitFor(textEmbedding);
        webApp.WithReference(chat).WaitFor(chat);

        return builder;
    }

    /// <summary>
    /// Configures eShop projects to use Ollama for text embedding and chat.
    /// </summary>
    public static IDistributedApplicationBuilder AddOllama(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> catalogApi,
        IResourceBuilder<ProjectResource> webApp)
    {
        var ollama = builder.AddOllama("ollama")
            .WithDataVolume()
            .WithGPUSupport()
            .WithOpenWebUI();
        var embeddings = ollama.AddModel("embedding", "all-minilm");
        var chat = ollama.AddModel("chat", "llama3.1");

        catalogApi.WithReference(embeddings)
            .WithEnvironment("OllamaEnabled", "true")
            .WaitFor(embeddings);
        webApp.WithReference(chat)
            .WithEnvironment("OllamaEnabled", "true")
            .WaitFor(chat);

        return builder;
    }

    public static IResourceBuilder<YarpResource> ConfigureMobileBffRoutes(this IResourceBuilder<YarpResource> builder,
        IResourceBuilder<ProjectResource> identityApi)
    {
        return builder.WithConfiguration(yarp =>
        {
            // Catalog and ordering calls from the mobile apps are graft WebSocket calls.
            // Identity stays on HTTP. The BFF still strips /identity for the OIDC authority
            // when a client is pointed at the BFF instead of Identity directly.
            yarp.AddRoute("/identity/{*any}", identityApi.GetEndpoint("http"))
                .WithTransformPathRemovePrefix("/identity");
        });
    }
}
