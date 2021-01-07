using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autofac;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using osu.Game.Online.API;
using Pisstaube.CacheDb;
using Pisstaube.Core.Database;
using Pisstaube.Core.Engine;
using Pisstaube.Core.Events;
using Pisstaube.Core.Utils;
using Pisstaube.Crawler.Crawlers;
using Pisstaube.Crawler.Online;
using Pisstaube.Online;
using Pisstaube.Utils;
using StackExchange.Redis;

#nullable enable

namespace Pisstaube.Crawler
{
    internal static class Program
    {
        
        private static void Main(string[] args)
        {
            var startup = new Startup();
            DbContextPool<PisstaubeDbContext>? dbContextPool = null;
            
            var builder = new ContainerBuilder();
            startup.ConfigureServices(builder);
            
            builder.Register(c =>
            {
                var pool = c.Resolve<DbContextPool<PisstaubeDbContext>>();

                var context = pool.Rent();

                return context;
            }).As<PisstaubeDbContext>()
                .OnRelease(c =>
                {
                    dbContextPool?.Return(c);
                });
            
            builder.Register(c => {
                var contextPool = c.Resolve<DbContextPool<PisstaubeDbContext>>();
                dbContextPool = contextPool;
                
                
                var apiProvider = c.Resolve<IAPIProvider>();
                var cacheDbContextFactory = c.Resolve<PisstaubeCacheDbContextFactory>();
                var searchEngine = c.Resolve<IBeatmapSearchEngineProvider>();
                var dbContext = c.Resolve<PisstaubeDbContext>();

                startup.Configure(apiProvider, cacheDbContextFactory, searchEngine, dbContext);
                
                return startup;
            }).AutoActivate();
            
            var container = builder.Build();

            var osuCrawler = container.Resolve<OsuCrawler>();
            var houseKeeper = container.Resolve<DatabaseHouseKeeper>();
            var redis = container.Resolve<ConnectionMultiplexer>();
            var setDownloader = container.Resolve<SetDownloader>();

            if (Environment.GetEnvironmentVariable("CRAWLER_DISABLED")?.ToLower() != "true")
                osuCrawler.Start();
            
            if (Environment.GetEnvironmentVariable("UPDATER_DISABLED")?.ToLower() != "true")
                houseKeeper.Start();

            var db = redis.GetDatabase(int.Parse(Environment.GetEnvironmentVariable("REDIS_DATABASE") ?? "-1"));
            var sub = redis.GetSubscriber();

            Dictionary<string, bool> alreadySet = new();

            sub.Subscribe($"__keyspace@{db.Database}__:*", (channel, value) =>
            {
                var key = channel.ToString().TrimStart($"__keyspace@{db.Database}__".ToArray()).TrimStart(':');

                // Ignore duplicates
                if (alreadySet.ContainsKey(key))
                {
                    alreadySet.Remove(key);
                    return;
                }
                
                if (key.StartsWith("pisstaube:events:downloads:"))
                {
                    Console.WriteLine("Download was requested...");

                    var data = db.StringGet(key);
                    Console.WriteLine(key);
                    Console.WriteLine(data);
                    
                    var request = JsonUtil.Deserialize<DownloadMapRequest>(data);

                    try
                    {
                        var response = setDownloader.DownloadMap(request.SetId, request.NoVideo);
                        db.StringSet(key, JsonUtil.Serialize(response));
                        alreadySet.Add(key, true);
                        Console.WriteLine("Success");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failure");
                        Console.WriteLine(ex);
                        db.KeyDelete(key);
                    }
                }

                Console.WriteLine("___________________________________");
            });
            
            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}