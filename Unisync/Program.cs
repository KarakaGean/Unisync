using Newtonsoft.Json;
using Serilog;

namespace Unisync
{
	internal class Program
	{
		private static readonly string CONFIG_PATH = "unisyncd.conf";
		private const string LOG_TEMPLATE = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
		private static Config? _config;
		private static JsonSerializerSettings _serializerSettings = new JsonSerializerSettings()
		{
			Formatting = Formatting.Indented,
		};

		static void Main(string[] args)
		{
			// Initialize serilog file logger
			Log.Logger = new LoggerConfiguration()
				.WriteTo.Console(outputTemplate: LOG_TEMPLATE)
				.CreateLogger();

			string currentDirectory = Directory.GetCurrentDirectory();
			string configPath = Path.Combine(currentDirectory, CONFIG_PATH);

			if (!File.Exists(configPath))
			{
				createDefaultOption(configPath);
				return;
			}

			string config = File.ReadAllText(CONFIG_PATH);
			_config = JsonConvert.DeserializeObject<Config>(config, _serializerSettings);

			if (_config == null )
			{
				createDefaultOption(configPath);
				return;
			}

			if (_config.LogToFile)
			{
				Log.Logger = new LoggerConfiguration()
					.WriteTo.Console(outputTemplate: LOG_TEMPLATE)
					.WriteTo.File("unisync.log",
								  rollingInterval: RollingInterval.Infinite,
								  outputTemplate: LOG_TEMPLATE,
								  fileSizeLimitBytes: 1024 * 1024 * 4, // 4mb
								  retainedFileCountLimit: 5,
								  rollOnFileSizeLimit: true)
					.CreateLogger();
			}

			List<Syncer> syncers = new List<Syncer>();

			Log.Information($"Initialize syncer by configuration...");

			foreach (SyncOption option in _config.options)
			{
				Syncer syncer = new Syncer(option.Group);
				syncer.Start(option);
				syncers.Add(syncer);
			}

			Log.Information($"Start Unisync");

			while (true)
			{
				Thread.Sleep(100);
				foreach (var syncer in syncers)
				{
					try
					{
						syncer.OnProcessTick();
					}
					catch (Exception ex)
					{
						Log.Fatal(ex.Message);
					}
				}
			}
		}

		private static void createDefaultOption(string configPath)
		{
			_config = new Config();
			SyncOption optionFormat = new()
			{
				Group = "Test Group",
				Tag = "TestSync",
				SourcePath = @"C:\\TestDirSrc",
				TargetPath = @"C:\\TestDirDest",
				IncludeExtensionFilters = new()
					{
						".cs"
					},
				DiffCheckIntervalSec = 0,
			};
			_config.options.Add(optionFormat);

			string configData = JsonConvert.SerializeObject(_config, _serializerSettings);
			File.WriteAllText(configPath, configData);

			Log.Warning($"There is no configuration at : {configPath}");
			Log.Information($"Create default configuration file.");
			Log.Information($"Try again when it's fill up.");
            Console.ReadLine();
        }
	}
}
