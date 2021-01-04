using System;
using System.IO;
using System.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Online.API;
using Pisstaube.Allocation;
using Pisstaube.CacheDb;
using Pisstaube.Crawler;
using Pisstaube.Database;
using Pisstaube.Engine;
using Pisstaube.Online;
using Pisstaube.Online.Crawler;
using Pisstaube.Utils;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Storage;
using StatsdClient;

namespace Pisstaube
{
    public class Startup
    {
        private readonly Storage _dataStorage = new NativeStorage("data");
        private readonly DatabaseContextFactory _osuContextFactory;

        private ILifetimeScope AutofacContainer { get; set; }
        
        // ReSharper disable once UnusedParameter.Local
        public Startup(IConfiguration configuration)
        {
            _osuContextFactory = new DatabaseContextFactory(_dataStorage);
        }
        
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();
            services.AddRouting();

            services
                .AddMvc(
                    options =>
                    {
                        options.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>();
                        options.EnableEndpointRouting = false;
                    })
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            
            services.AddRouting();
            services.AddDbContextPool<PisstaubeDbContext>(optionsBuilder => {
                var host = Environment.GetEnvironmentVariable("MARIADB_HOST");
                var port = Environment.GetEnvironmentVariable("MARIADB_PORT");
                var username = Environment.GetEnvironmentVariable("MARIADB_USERNAME");
                var password = Environment.GetEnvironmentVariable("MARIADB_PASSWORD");
                var db = Environment.GetEnvironmentVariable("MARIADB_DATABASE");

                optionsBuilder.UseMySql(
                    $"Server={host};Database={db};User={username};Password={password};Port={port};CharSet=utf8mb4;SslMode=none;",
                    mysqlOptions =>
                    {
                        mysqlOptions.ServerVersion(new Version(10, 4, 12), ServerType.MariaDb);
                        mysqlOptions.CharSet(CharSet.Utf8Mb4);
                    }
                );
            });

            services.AddStackExchangeRedisCache(ops =>
            {
                var host = Environment.GetEnvironmentVariable("REDIS_HOST");
                var port = Environment.GetEnvironmentVariable("REDIS_PORT");
                var pass = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
                var database = Environment.GetEnvironmentVariable("REDIS_DATABASE");
                
                var connString = $"{host}";
                if (!string.IsNullOrEmpty(port))
                    connString += $":{port}";
                if (!string.IsNullOrEmpty(pass))
                    connString += $",password={pass}";
                if (!string.IsNullOrEmpty(database))
                    connString += $",defaultDatabase={database}";
                
                ops.Configuration = connString;
            });
            
            var builder = new ContainerBuilder();
            
            builder.Populate(services);
            builder.RegisterType<OsuCrawler>().AsSelf().As<ICrawler>().SingleInstance();
            builder.RegisterType<DatabaseHouseKeeper>().SingleInstance();
            
            builder.RegisterType<IpfsCache>().SingleInstance();
            
            builder.RegisterInstance(new RequestLimiter(1200, TimeSpan.FromMinutes(1)));
            builder.RegisterInstance(_dataStorage).As<Storage>();
            builder.RegisterInstance(_osuContextFactory).As<IDatabaseContextFactory>();
            builder.RegisterType<FileStore>();
            builder.RegisterType<PisstaubeCacheDbContextFactory>().AsSelf();
            builder.RegisterType<SetDownloader>().AsSelf();
            builder.RegisterType<SmartStorage>().AsSelf();

            builder.RegisterType<BeatmapSearchEngine>().As<IBeatmapSearchEngineProvider>();
            builder.RegisterType<BeatmapDownloader>();

            builder.RegisterType<OsuConfigManager>();
            builder.RegisterType<APIAccess>().As<IAPIProvider>().SingleInstance();

            AutofacContainer = builder.Build();
            
            return new AutofacServiceProvider(AutofacContainer);
        }


        private static void MetricUpdater(IAPIProvider provider, SmartStorage smartStorage, ICrawler osuCrawler, DatabaseHouseKeeper houseKeeper)
        {
            while (true)
            {
                DogStatsd.ServiceCheck("is_active", Status.OK);
                DogStatsd.ServiceCheck("is_crawling", GlobalConfig.EnableCrawling ? Status.OK : Status.WARNING); 
                DogStatsd.ServiceCheck("is_search_enabled", GlobalConfig.EnableSearch ? Status.OK : Status.CRITICAL); 
                DogStatsd.ServiceCheck("is_updating_enabled", GlobalConfig.EnableUpdating ? Status.OK : Status.CRITICAL);
                DogStatsd.ServiceCheck("osu_api_status", provider?.State switch
                {
                    APIState.Offline => Status.CRITICAL,
                    APIState.Failing => Status.CRITICAL,
                    APIState.Connecting => Status.WARNING,
                    APIState.Online => Status.OK,
                    _ => Status.UNKNOWN
                });
                
                DogStatsd.Set("smart_storage.free_space", smartStorage.DataDirectorySize / (double) smartStorage.MaxSize);
                DogStatsd.Set("smart_storage.max_space", smartStorage.MaxSize);
                
                DogStatsd.Set("osu_crawler.latest_id", osuCrawler.LatestId);
                DogStatsd.Set("house_keeper.to_update", houseKeeper.ToUpdate);
                DogStatsd.Set("house_keeper.remaining", houseKeeper.Remaining);

                Thread.Sleep(TimeSpan.FromMinutes(1));
            }
        }


        [UsedImplicitly]
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,
            ICrawler crawler, IAPIProvider apiProvider, DatabaseHouseKeeper houseKeeper,
            PisstaubeCacheDbContextFactory cacheDbContextFactory, IBeatmapSearchEngineProvider searchEngine,
            SmartStorage smartStorage, PisstaubeDbContext dbContext)
        {
            Logger.Enabled = true;
            Logger.Level = LogLevel.Debug;
            Logger.GameIdentifier = "Pisstaube";
            Logger.Storage = _dataStorage.GetStorageForDirectory("logs");

            dbContext.Database.Migrate();
            
            while (!searchEngine.IsConnected)
            {
                Logger.LogPrint("Search Engine is not yet Connected!", LoggingTarget.Database, LogLevel.Important);
                Thread.Sleep(1000);
            }

            cacheDbContextFactory.Get().Migrate();
            _osuContextFactory.Get().Migrate();

            DogStatsd.Configure(new StatsdConfig {Prefix = "pisstaube"});

            new Thread(() => MetricUpdater(apiProvider, smartStorage, crawler, houseKeeper)).Start();
            
            JsonUtil.Initialize();

            apiProvider.Login(Environment.GetEnvironmentVariable("OSU_USERNAME"),
                Environment.GetEnvironmentVariable("OSU_PASSWORD"));

            GlobalConfig.EnableCrawling = Environment.GetEnvironmentVariable("CRAWLER_DISABLED")?.ToLowerInvariant() == "false";
            GlobalConfig.EnableUpdating = Environment.GetEnvironmentVariable("UPDATER_DISABLED")?.ToLowerInvariant() == "false";
            
            while (true)
            {
                if (!apiProvider.IsLoggedIn)
                {
                    Logger.LogPrint("Not Logged in yet...");
                    Thread.Sleep(1000);
                    continue;
                }
                if (apiProvider.State == APIState.Failing)
                {   
                    Logger.LogPrint($"Failed to Login using Username {Environment.GetEnvironmentVariable("OSU_USERNAME")}", LoggingTarget.Network, LogLevel.Error);
                    Environment.Exit(1);
                }

                break;
            }

            if (DebugUtils.IsDebugBuild)
                app.UseDeveloperExceptionPage();
            else
                app.UseHsts();
            
            if (GlobalConfig.EnableCrawling)
                crawler.Start();
            
            if (GlobalConfig.EnableUpdating)
                houseKeeper.Start();
            
            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Join(Directory.GetCurrentDirectory(), "data/wwwroot")),
                EnableDirectoryBrowsing = true,
            });

            app.UseMvc(routes => routes.MapRoute("default", "{controller=Home}/{action=Index}/{id?}"));
        }
    }
}