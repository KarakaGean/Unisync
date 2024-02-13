using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;
using static System.Net.Mime.MediaTypeNames;

namespace Unisync
{
	internal class Program
	{
		private static readonly string CONFIG_PATH = "unisyncd.conf";
		private const string LOG_TEMPLATE = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
		static void Main(string[] args)
		{

			//var extension = Path.GetExtension(@"test - 복사본 (4).cs~RF1ced133.TMP");
   //         Console.WriteLine(extension);

			//string fileA = $@"C:\\TestDirSrc\big1.cs";
			//string fileB = $@"C:\\TestDirSrc\big2.cs";

			string fileA = $@"C:\\TestDirSrc\small1.txt";
			string fileB = $@"C:\\TestDirSrc\small2.txt";

			//const int TestCount = 1000;

			//Stopwatch sw = new Stopwatch();
			//sw.Restart();
			//for (int i = 0; i < TestCount; i++)
			//{
			//	if (!FileManager.CompareBigFiles(fileA, fileB))
			//	{
   //                 //Console.WriteLine("ERROR");
   //             }
			//}
   //         Console.WriteLine(sw.ElapsedMilliseconds);

   //         Console.WriteLine();

   //         sw.Restart();
			//for (int i = 0; i < TestCount; i++)
			//{
			//	if (!FileManager.CompareSmallFiles(fileA, fileB))
			//	{
			//		//Console.WriteLine("ERROR");
			//	}
			//}
			//Console.WriteLine(sw.ElapsedMilliseconds);


			// Initialize serilog file logger
			Log.Logger = new LoggerConfiguration()
#if DEBUG
				.WriteTo.Console(outputTemplate: LOG_TEMPLATE)
				.CreateLogger();
#else
				.WriteTo.File("unisync.log",
							  rollingInterval: RollingInterval.Infinite,
							  outputTemplate: LOG_TEMPLATE,
							  fileSizeLimitBytes: 1024 * 1024 * 4, // 4mb
							  retainedFileCountLimit: 5,
							  rollOnFileSizeLimit: true)
				.CreateLogger();
#endif

			Syncer syncer = new Syncer("TestGroup");
			SyncOption option = new()
			{
				Tag = "TestSync",
				SourcePath = @"C:\\TestDirSrc",
				TargetPath = @"C:\\TestDirDest",
				IncludeExtensionFilters = new()
				{
					".cs"
				},
				DiffCheckIntervalSec = 0,
			};
			syncer.Start(option);

			while (true)
			{
				Thread.Sleep(100);
				syncer.OnProcessTick();
			}
		}
	}
}
