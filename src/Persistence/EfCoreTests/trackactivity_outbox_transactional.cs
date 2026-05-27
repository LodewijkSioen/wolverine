using Alba;
using IntegrationTests;
using JasperFx.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedPersistenceModels.Items;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.SqlServer;
using Wolverine.Tracking;
using Xunit.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace EfCoreTests;

[Collection("sqlserver")]
public class trackactivity_outbox_transactional(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UsingTransactions(bool useTransactions)
    {
        var builder = CreateBuilder(useTransactions);
        var host = await AlbaHost.For(builder, app =>
        {
            app.MapWolverineEndpoints();
        });

        var tracked1 = await host
            .TrackActivity()
            .ExecuteAndWaitAsync(_ =>
            {
                return host.Scenario(s =>
                {
                    s.Post.Json(new { Name = "One" }).ToUrl("/leakysessions");
                });
            });

        var tracked2 = await host
            .TrackActivity()
            .ExecuteAndWaitAsync(_ =>
            {
                return host.Scenario(s =>
                {
                    s.Post.Json(new { Name = "One" }).ToUrl("/leakysessions");
                });
            });

        output.WriteLine("TRACKED SESSION 1");
        output.WriteLine(tracked1.ToString());
        output.WriteLine("---------------------------------------------");
        output.WriteLine("TRACKED SESSION 2");
        output.WriteLine(tracked2.ToString());

        tracked1.MessageSucceeded.SingleMessage<OutboxHandler.Message>();
        tracked2.MessageSucceeded.SingleMessage<OutboxHandler.Message>();
    }

    private WebApplicationBuilder CreateBuilder(bool useTransactions)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.DisableConventionalDiscovery()
                .IncludeType(typeof(TestHandler))
                .IncludeType(typeof(OutboxHandler));

            opts.Services.AddDbContextWithWolverineIntegration<ItemsDbContext>(x =>
            {
                x.UseSqlServer(Servers.SqlServerConnectionString);
            }, "wolverine");

            if (useTransactions)
            {
                opts.Policies.AutoApplyTransactions();
            }

            opts.PersistMessagesWithSqlServer(Servers.SqlServerConnectionString, "wolverine");

            opts.Services.RunWolverineInSoloMode();
            opts.Services.AddResourceSetupOnStartup();
            opts.Services.DisableAllExternalWolverineTransports();
        });
        builder.Services.AddWolverineHttp();

        return builder;
    }
}


public class TestHandler
{
    public record Message(string Name);

    [WolverinePost("/leakysessions")]
    public async Task<(IResult, OutgoingMessages)> Handle([FromServices] ItemsDbContext dbContext, ILogger logger)
    {
        await dbContext.Items.AddAsync(new(){Name = "name"});
        logger.LogInformation("Handling TestHandler Message");
        return (Results.Ok("ok"), [new OutboxHandler.Message("test")]);
    }
}

public class OutboxHandler
{
    public record Message(string Name);

    public void Handle(Message message, ILogger logger)
    {
        logger.LogInformation($"Handling Outbox Message: {message.Name}");
    }
}