namespace CoolingControl;

using CoolingControl.Platform.LHM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// The main entry point for the application.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        string? cmd = args.Length >= 1 ? args[0] : null;
        
        // Check for --help or -h
        if (cmd == "help" || cmd == "-h")
        {
            PrintHelp();
            return;
        }

        Directory.SetCurrentDirectory(AppContext.BaseDirectory); // Set current directory to the executable's location        

        // Initialize Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/cooling_control.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

         // Read configuration from config.json
        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config/config.json"))
                     ?? throw new InvalidOperationException("Deserialized configuration is null.");
            Log.Information("Configuration loaded successfully from config.json");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read or parse config.json");
            return; // Exit the application
        }

        // Reinitialize Serilog
        Log.CloseAndFlush();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(config.LogLevel))
            .Enrich.FromLogContext()        
            .WriteTo.Console()
            .WriteTo.File("logs/cooling_control.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Check for list-sensors command-line option
        if (cmd == "list-sensors")
        {
            Log.Information("Listing all available sensors");
            using (var coolingControl = new LHMAdapter(config))
            {
                coolingControl.ListAllSensors();
            }
            Log.Information("Sensor listing complete");
            // Exit after listing sensors
            return;
        }

        // Check for calibrate command-line option
        if (cmd == "calibrate")
        {
            string param = args.Length >= 2 ? args[1] : "all";
            try
            {
                ControlCalibratorBuilder(args, config, param).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error");
                return; // Exit the application
            }

            // Exit after calibration
            return;
        }

        // Service handling
        try
        {
            CoolingControlDaemonBuilder(args, config).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error");
            return; // Exit the application
        }
    }

    private static IHostBuilder CoolingControlDaemonBuilder(string[] args, Config config) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseWindowsService(options =>
            {
                options.ServiceName = "CoolingControl";
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(config);
                services.AddHostedService<CoolingControlDaemon>();
            });


    private static IHostBuilder ControlCalibratorBuilder(string[] args, Config config, string control_alias) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(config);
                services.AddSingleton(control_alias);
                services.AddHostedService<ControlCalibration>();
            });

    private static void PrintHelp()
    {
        string version = FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion ?? "unknown";
  
        Console.WriteLine($"CoolingControl v{version}");
        Console.WriteLine($"Runtime Version: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine("Usage:");
        Console.WriteLine("  coolingcontrol.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  help, -h         Display this help message and exit.");
        Console.WriteLine("  list-sensors     List all available hardware sensors and exit.");
        Console.WriteLine();
        Console.WriteLine("Service Management:");
        Console.WriteLine("  To manage the service, use sc.exe commands as an administrator:");
        Console.WriteLine("    Start:   sc start CoolingControl");
        Console.WriteLine("    Stop:    sc stop CoolingControl");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Run as administrator to access hardware sensors.");
        Console.WriteLine("  - Use list-sensors to discover sensor names for config.json.");
        Console.WriteLine("  - In console mode, press Ctrl+C to stop the daemon.");
        Console.WriteLine("  - Configuration is loaded from config.json.");
    }
}

