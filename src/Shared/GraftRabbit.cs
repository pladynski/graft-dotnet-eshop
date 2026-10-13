// Shared RabbitmqPlugin client JSON. Service Bus later means a different plugin name and fields.
namespace eShop.Graft;

public static class GraftRabbit
{
    public const string TransportVariable = "ESHOP_GRAFT_TRANSPORT";

    public static bool TransportEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(TransportVariable), "rabbitmq", StringComparison.OrdinalIgnoreCase);

    public static string ClientConfig(string graftName, string queue)
    {
        var host = Environment.GetEnvironmentVariable("ESHOP_GRAFT_PLUGIN_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost:5672";
        }

        var user = Environment.GetEnvironmentVariable("ESHOP_GRAFT_PLUGIN_USER");
        if (string.IsNullOrWhiteSpace(user))
        {
            user = "guest";
        }

        var password = Environment.GetEnvironmentVariable("ESHOP_GRAFT_PLUGIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            password = "guest";
        }

        var plugin = Environment.GetEnvironmentVariable("ESHOP_GRAFT_PLUGIN_NAME");
        if (string.IsNullOrWhiteSpace(plugin))
        {
            plugin = "RabbitmqPlugin";
        }

        var reply = queue + ".reply";
        return $$"""
        {
          "configurations": {
            "{{graftName}}": {
              "runtime": "netcore",
              "host": "{{host.Trim()}}",
              "stateless": true,
              "plugin": {
                "name": "{{plugin.Trim()}}",
                "queue": "{{queue}}",
                "replyQueue": "{{reply}}",
                "user": "{{user}}",
                "password": "{{password}}",
                "vhost": "/",
                "rpcTimeoutMs": 30000
              }
            }
          }
        }
        """;
    }
}
