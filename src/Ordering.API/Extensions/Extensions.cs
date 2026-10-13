using FluentValidation;

internal static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        
        // Order HTTP routes are gone. Graft methods validate the caller JWT themselves.
        // There is no endpoint left that needs JWT bearer middleware.

        // Pooling is disabled because of the following error:
        // Unhandled exception. System.InvalidOperationException:
        // The DbContext of type 'OrderingContext' cannot be pooled because it does not have a public constructor accepting a single parameter of type DbContextOptions or has more than one constructor.
        services.AddDbContext<OrderingContext>(options =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString("orderingdb"));
        });
        builder.EnrichNpgsqlDbContext<OrderingContext>();

        services.AddMigration<OrderingContext, OrderingContextSeed>();

        // Add the integration services that consume the DbContext
        services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<OrderingContext>>();

        services.AddTransient<IOrderingIntegrationEventService, OrderingIntegrationEventService>();

        services.AddHttpContextAccessor();
        services.AddTransient<IIdentityService, IdentityService>();

        // Configure mediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining(typeof(Program));

            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // Register the command validators for the validator behavior (validators based on FluentValidation library)
        services.AddValidatorsFromAssemblyContaining<CancelOrderCommandValidator>();

        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IBuyerRepository, BuyerRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IRequestManager, RequestManager>();
    }
}
