using BotServer;
using MementoMori.Apis;
using MementoMori.BotServer.Options;
using MementoMori.NetworkInterceptors;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data.Notice;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Quartz.Impl.AdoJobStore;
using Refit;

namespace MementoMori.BotServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.ConfigureWritable<AuthOption>(context.Configuration.GetSection("Auth"));
                    services.ConfigureWritable<BotOptions>(context.Configuration.GetSection("Bot"));
                    services.ConfigureWritable<GameConfig>(context.Configuration.GetSection("Game"));
                    IFileProvider physicalProvider = new PhysicalFileProvider(Directory.GetCurrentDirectory());
                    services.AddSingleton(physicalProvider);

                    Func<IServiceProvider, IFreeSql> fsqlFactory = r =>
                    {
                        IFreeSql fsql = new FreeSql.FreeSqlBuilder()
                            .UseConnectionString(FreeSql.DataType.Sqlite, @"Data Source=mmmr_bot.db")
                            .UseMonitorCommand(cmd => Console.WriteLine($"Sql：{cmd.CommandText}"))
                            .UseAutoSyncStructure(true)
                            .Build();
                        return fsql;
                    };
                    services.AddSingleton(fsqlFactory);

                    services.AddHttpClient();
                    services.AddSingleton<IMemeMoriServerApi>((sp => RestService.For<IMemeMoriServerApi>("http://localhost:5000")));
                    services.AddSingleton<BattleLogManager>();
                    services.AddSingleton<BattleLogInterceptor>();
                    services.AddSingleton<MementoNetworkManager>();
                    services.Discover();
                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
    }
}