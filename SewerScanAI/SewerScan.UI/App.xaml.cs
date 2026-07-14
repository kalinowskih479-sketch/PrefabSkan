using System;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SewerScan.Infrastructure.DependencyInjection;

namespace SewerScan.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Build configuration from appsettings.json so Serilog can be initialized early
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // Configure Serilog static logger
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) => { config.AddConfiguration(configuration); })
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // Register infrastructure
                    services.AddInfrastructure(context.Configuration);

                    // Register UI types
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.Start();

            var main = _host.Services.GetRequiredService<MainWindow>();
            main.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
