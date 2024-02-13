using Serilog;

namespace Unisync
{
	public static class Global
	{
		public const int WATCHER_MAX_BUFFER_SIZE = 4096;
		public const int WATCHER_MAX_EVENT_COUNT = 4096 / 16; // 256

		public static class Filter
		{
			public static bool CheckFileter(string path, bool isDirectory, ref SyncOption syncOption)
			{
				path = path.ToLower();

				if (isDirectory)
				{
					foreach (string exDir in syncOption.ExcludeDirectories)
					{
						if (path.Contains(exDir.ToLower()))
							return false;
					}
				}
				else
				{
					string ext = Path.GetExtension(path).ToLower();
					foreach (string include in syncOption.IncludeExtensionFilters)
					{
						if (ext != include.ToLower())
							return false;
					}
				}

				return true;
			}
		}
	}
}
