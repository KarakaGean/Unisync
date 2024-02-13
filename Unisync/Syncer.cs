using System.Collections.Concurrent;
using Microsoft.Extensions.FileSystemGlobbing;
using Serilog;

namespace Unisync
{
	public class Syncer
	{
        public readonly string Group;

        public string SourcePath { get; private set; } = string.Empty;
        public string TargetPath { get; private set; } = string.Empty;

        private SyncWatcher? _watcher;
		private SyncOption? _option;

		private object _lock = new object();
        private ConcurrentQueue<SyncWatchEvents> _jobQueue;
        private ConcurrentQueue<SyncWatchEvents> _jobQueueBuffer;

		private Action<SyncWatchEvents> _pushJob;
		private Action<string> _warnOutput;

		public Syncer(string gruop)
        {
			Group = gruop;
			_jobQueue = new ConcurrentQueue<SyncWatchEvents>();
			_jobQueueBuffer = new ConcurrentQueue<SyncWatchEvents>();

			_pushJob = pushjob;
			_warnOutput = onWarnOutput;
		}

		private void onWarnOutput(string message)
		{
			Log.Warning($"[Group:{Group}] {message}");
		}

		public void Start(SyncOption option)
		{
			_watcher?.Dispose();

			_option = option;
			SyncWatcherIdentification swId = new(_option.Value.Tag, _option.Value.Group);
			_watcher = new SyncWatcher(swId, _pushJob);
			_watcher.Start(_option.Value);
		}

		private void pushjob(SyncWatchEvents job)
		{
			_jobQueue.Enqueue(job);
		}

		public void OnProcessTick()
		{
            if (_jobQueue.Count <= 0)
                return;

			lock (_lock)
			{
				var temp = _jobQueue;
				_jobQueue = _jobQueueBuffer;
				_jobQueueBuffer = temp;
			}

			bool needCheckAll = false;
			while (_jobQueueBuffer.TryDequeue(out var job))
			{
				Log.Information(job.ToString());

				if (!job.IsValidPath())
				{
					Log.Error($"[Group:{Group}] IT'S ROOT DIRECTORY OR EMPTY!\nJob : {job}");
					throw new Exception("IT'S ROOT DIRECTORY OR EMPTY!");
				}

				switch (job.WatchType)
				{
					case SyncWatchType.CheckAll:
						needCheckAll |= true; break;

					case SyncWatchType.Created:
                        needCheckAll |= !tryHandle_Created(job);
                        break;

					case SyncWatchType.Modified:
						needCheckAll |= !tryHandle_Modified(job);
						break;

					case SyncWatchType.Deleted:
						needCheckAll |= !tryHandle_Deleted(job);
						break;

					case SyncWatchType.Renamed:
						needCheckAll |= !tryHandle_Renamed(job);
						break;

					case SyncWatchType.Disposed:
					case SyncWatchType.Error:
					case SyncWatchType.Error_SourcePath:
					case SyncWatchType.Error_TargetPath:
						needCheckAll |= true;
						break;
				}
            }

			if (needCheckAll)
			{
				CheckDifferences();
			}
		}

		private bool tryHandle_Created(SyncWatchEvents syncJob)
        {
			var srcPath = syncJob.SourceFullPath;
			var tarPath = syncJob.TargetFullPath;

			if (syncJob.IsDirection)
			{
				if (!FileManager.TryCopyDirectory(srcPath, tarPath, _warnOutput, shouldCopyFile, out Exception? ex))
				{
					Log.Error($"[Group:{Group}] Create handle error!\nException : {ex.Message}");
					return false;
				}
			}
			else // if it's file
			{
				if (!FileManager.TryCopyFile(srcPath, tarPath, _warnOutput, out Exception? ex))
				{
					Log.Error($"[Group:{Group}] Create handle error!\nException : {ex.Message}");
					return false;
				}
			}

			return true;
		}

		private bool tryHandle_Modified(SyncWatchEvents syncJob)
		{
			if (syncJob.IsDirection)
				return true;

			var srcPath = syncJob.SourceFullPath;
			var tarPath = syncJob.TargetFullPath;

			FileInfo tarInfo = new FileInfo(tarPath);
			if (tarInfo.Exists)
			{
				if (FileManager.CompareBigFiles(srcPath, tarPath))
				{
					return true;
				}
			}

			if (!FileManager.TryCopyFile(srcPath, tarPath, _warnOutput, out Exception? ex))
			{
				Log.Error($"[Group:{Group}] Modifie handle error!\nException : {ex.Message}");
				return false;
			}

			return true;
		}

		private bool tryHandle_Deleted(SyncWatchEvents syncJob)
		{
			string tarDirPath = syncJob.TargetFullPath;

			if (syncJob.IsDirection)
			{
				try
				{
					if (!Directory.Exists(tarDirPath))
					{
						Log.Warning($"[Group:{Group}] Delete handle warning! There is no directory to delete!");
						return false;
					}

					Directory.Delete(tarDirPath, recursive: true);
				}
				catch (Exception ex)
				{
					Log.Error($"[Group:{Group}] Delete handle error!\nException : {ex.Message}");
				}
			}
			else
			{
				try
				{
					if (!File.Exists(tarDirPath))
					{
						Log.Warning($"[Group:{Group}] Delete handle warning! There is no file to delete!");
						return false;
					}

					File.Delete(tarDirPath);
				}
				catch (Exception ex)
				{
					Log.Error($"[Group:{Group}] Delete handle error!\nException : {ex.Message}");
				}
			}

			return true;
		}

		private bool tryHandle_Renamed(SyncWatchEvents syncJob)
		{
			string tarNewPath = syncJob.TargetFullPath;
			string tarOldPath = syncJob.TargetOldFullPath;

			if (syncJob.IsDirection)
			{
				try
				{
					Directory.Move(tarOldPath, tarNewPath);
				}
				catch (Exception ex)
				{
					Log.Error($"[Group:{Group}] Rename handle error!\nException : {ex.Message}");
					return false;
				}
			}
			else
			{
				try
				{
					if (File.Exists(tarNewPath))
					{
						if (File.Exists(tarOldPath))
						{
							File.Delete(tarOldPath);
						}
					}

					if (File.Exists(tarOldPath))
					{
						File.Move(tarOldPath, tarNewPath);
					}
					else
					{
						if (!FileManager.TryCopyFile(syncJob.SourceFullPath,
													 tarNewPath, _warnOutput,
													 out Exception? ex))
						{
							throw ex;
						}
					}
				}
				catch (Exception ex)
				{
					Log.Error($"[Group:{Group}] Rename handle error!\nException : {ex.Message}");
					return false;
				}
			}

			return true;
		}

        public void CheckDifferences()
		{
			Log.Information($"[Group:{Group}] Start diff check...");

			if (!_option.HasValue)
			{
				Log.Error($"[Group:{Group}] There is no option file!");
				return;
			}

			string sourcePath = _option.Value.SourcePath;
			string targetPath = _option.Value.TargetPath;

			Queue<string> srcRetrieve = new();
			srcRetrieve.Enqueue(sourcePath);

			while (srcRetrieve.TryDequeue(out string? curPath))
			{
				string relativeSourSrcPath = Path.GetRelativePath(sourcePath, curPath);

				if (string.IsNullOrWhiteSpace(curPath))
					continue;

				var srcDirs = Directory.GetDirectories(curPath);
				var srcFiles = Directory.GetFiles(curPath);
				HashSet<string> relativeSrcDirs = new();
				HashSet<string> relativeSrcFiles = new();

				foreach (string dir in srcDirs)
				{
					relativeSrcDirs.Add(Path.GetRelativePath(sourcePath, dir));
					srcRetrieve.Enqueue(dir);
				}
				foreach (string file in srcFiles)
				{
					relativeSrcFiles.Add(Path.GetRelativePath(sourcePath, file));
				}

				string targetCurPath = Path.Combine(targetPath, relativeSourSrcPath);

				if (!Directory.Exists(targetCurPath))
				{
					Directory.CreateDirectory(targetCurPath);
				}

				var tarDirs = Directory.GetDirectories(targetCurPath);
				var tarFiles = Directory.GetFiles(targetCurPath);
				HashSet<string> relativeTarDirs = new();
				HashSet<string> relativeTarFiles = new();

				foreach (string dir in tarDirs)
				{
					relativeTarDirs.Add(Path.GetRelativePath(targetPath, dir));
				}
				foreach (string file in tarFiles)
				{
					relativeTarFiles.Add(Path.GetRelativePath(targetPath, file));
				}

				// Delete all files or directories that exist only in the target.
				relativeTarDirs.ExceptWith(relativeSrcDirs);
				relativeTarFiles.ExceptWith(relativeSrcFiles);

				foreach (string td in relativeTarDirs)
				{
					try
					{
						Directory.Delete(td, recursive: true);
					}
					catch (Exception e)
					{
						Log.Error($"[Group:{Group}] Check Diff target directory remove error!\nException: {e}");
					}
				}

				foreach (string fd in relativeTarFiles)
				{
					try
					{
						File.Delete(fd);
					}
					catch (Exception e)
					{
						Log.Error($"[Group:{Group}] Check Diff target file remove error!\nException: {e}");
					}
				}

				// Copy or skip all files and directories if exits.
				foreach (string srcDir in srcDirs)
				{
					string tarDir = Path.Combine(targetPath, Path.GetRelativePath(sourcePath, srcDir));
					if (Directory.Exists(tarDir))
						continue;

					if (!FileManager.TryCopyDirectory(srcDir, tarDir, _warnOutput, shouldCopyFile, out Exception? e))
					{
						Log.Error($"[Group:{Group}] Check Diff target directory copy error!\nException: {e}");
					}

				}
				foreach (string srcFile in srcFiles)
				{
					string tarFile = Path.Combine(targetPath, Path.GetRelativePath(sourcePath, srcFile));
					if (File.Exists(tarFile) && FileManager.CompareSmallFiles(srcFile, tarFile))
						continue;

					if (!FileManager.TryCopyFile(srcFile, tarFile, _warnOutput, out Exception? e))
					{
						Log.Error($"[Group:{Group}] Check Diff target file copy error!\nException: {e}");
					}
				}
			}

			Log.Information($"[Group:{Group}] End diff check!");
		}

		private bool shouldCopyFile(string srcFilePath, string destFilePath)
		{
			if (!_option.HasValue)
				return false;
			var option = _option.Value;

			return Global.Filter.CheckFileter(srcFilePath, isDirectory: false, ref option) &&
				   Global.Filter.CheckFileter(destFilePath, isDirectory: false, ref option);
		}
	}
}
