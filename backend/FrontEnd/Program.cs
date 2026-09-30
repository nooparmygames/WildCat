using Frontend;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEnd
{
    public class Program
    {
        public static IClusterClient orleansClient { get; private set; } = null;
        private static string[] args;

        public static async Task Main(string[] args)
        {
            bool connected = false;
            Console.WriteLine("Starting Frontend");
            int i = 0;
            while (i < 10)
            {
                try
                {
                    ClusterClientHostedService service = new ClusterClientHostedService();
                    await service.StartAsync(CancellationToken.None);
                    connected = true;
                    orleansClient = service.Client;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"err{ex}");
                    i++;
                    await Task.Delay(3000);
                }
            }

            if (!connected)
                throw new System.Exception("Could not connect to silo. crashing the process");
            Console.WriteLine("Connected to silo");
            await CreateHostBuilder(args).RunConsoleAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseStartup<Startup>()
                        .UseKestrel();
                    }).ConfigureServices(s =>
                    {
                        s.AddSingleton<IClusterClient>(orleansClient);
                        s.Configure<ConsoleLifetimeOptions>(options =>
                        {
                            options.SuppressStatusMessages = true;
                        });
                    });
        }
    }
}
