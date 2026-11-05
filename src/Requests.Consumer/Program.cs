using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Requests.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<IConnection>(_ =>
{
    var f = new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMq:Host"] ?? "localhost",
        UserName = builder.Configuration["RabbitMq:User"] ?? "guest",
        Password = builder.Configuration["RabbitMq:Password"] ?? "guest",
        DispatchConsumersAsync = true,
        AutomaticRecoveryEnabled = true
    };
    return f.CreateConnection("requests-consumer");
});
builder.Services.AddHostedService<Requests.Consumer.Worker>();

var host = builder.Build();
host.Run();