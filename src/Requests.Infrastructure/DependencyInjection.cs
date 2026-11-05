using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Requests.Application.Interfaces;
using Requests.Application.Services;
using Requests.Infrastructure.Messaging;
using Requests.Infrastructure.Outbox;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Repositories;

namespace Requests.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(cfg.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure(5)));

        services.AddScoped<IRequestRepository, EfRequestRepository>();
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<RequestService>();

        // RabbitMQ connection (singleton, shared)
        services.AddSingleton<IConnection>(_ =>
        {
            var factory = new ConnectionFactory
            {
                HostName = cfg["RabbitMq:Host"] ?? "localhost",
                Port = int.Parse(cfg["RabbitMq:Port"] ?? "5672"),
                UserName = cfg["RabbitMq:User"] ?? "guest",
                Password = cfg["RabbitMq:Password"] ?? "guest",
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };
            return factory.CreateConnection("requests-api");
        });

        services.AddSingleton<IEventPublisher, RabbitMqPublisher>();
        services.Configure<OutboxOptions>(opt =>
        {
            var section = cfg.GetSection("Outbox");
            opt.BatchSize = int.TryParse(section["BatchSize"], out var b) ? b : 20;
            opt.MaxRetries = int.TryParse(section["MaxRetries"], out var r) ? r : 5;
            opt.ProcessingTimeoutMinutes =
                int.TryParse(section["ProcessingTimeoutMinutes"], out var t) ? t : 5;
        });
        services.AddHostedService<OutboxPublisherHostedService>();
        return services;
    }
}