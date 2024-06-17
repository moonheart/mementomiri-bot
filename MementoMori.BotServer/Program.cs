using BotServer;
using MementoMori.BotServer.Options;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data.Notice;
using Quartz.Impl.AdoJobStore;

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
                    services.AddSingleton<MementoNetworkManager>();
                    services.Discover();
                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
    }
}