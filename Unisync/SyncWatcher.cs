using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace Unisync
{
	public class SyncWatcher : IDisposable
	{
		public SyncWatcherIdentification SyncID { get; private set; }
		private FileSystemWatcher? _sourceDirWatcher;
		private FileSystemWatcher? _targetDirWatcher;
		private FileSystemWatcher? _sourceWatcher;
		public SyncOption? Option { get; private set; }
		private System.Timers.Timer _retryWatchTimer;
		private System.Timers.Timer _checkDiffAllTimer;

		public string SourcePath { get; private set; } = string.Empty;
		public string TargetPath { get; private set; } = string.Empty;

		public bool IsRetrying => _retryingCount > 0;
		private int _retryingCount = 0;

		public bool IsEnabled { get; private set; } = false;
		private Action<SyncWatchEvents>? _onFileStateChanged;

		public SyncWatcher(SyncWatcherIdentification syncID,
						   Action<SyncWatchEvents> onFileStateChanged)
		{
			SyncID = syncID;
			_onFileStateChanged = onFileStateChanged;
			_retryWatchTimer = new System.Timers.Timer();
			_retryWatchTimer.AutoReset = true;
			_retryWatchTimer.Enabled = false;
			_retryWatchTimer.Elapsed += onRetryWatch;

			_checkDiffAllTimer = new System.Timers.Timer();
			_checkDiffAllTimer.AutoReset = true;
			_checkDiffAllTimer.Interval = int.MaxValue;
			_checkDiffAllTimer.Enabled = true;
			_checkDiffAllTimer.Elapsed += onCheckDiffAll;
		}

		public void Start(SyncOption option)
		{
			if (IsEnabled)
			{
				Dispose();
			}

			IsEnabled = false;

			Option = option;
			SyncOption opInst = Option.Value;

			// Set timer
			int intervalSec = opInst.RetryIntervalSec;
			_retryWatchTimer.Interval = intervalSec * 1000;

			// Set paths
			SourcePath = opInst.SourcePath;
			TargetPath = opInst.TargetPath;

			// Verify if the paths are valid
			bool isSourceExist = Directory.Exists(SourcePath);
			if (!isSourceExist)
				Log.Error($"[{SyncID}] There is no source path : {SourcePath}");

			bool isTargetExist = Directory.Exists(TargetPath);
			if (!isTargetExist)
				Log.Error($"[{SyncID}] There is no source path : {SourcePath}");

			if (!isSourceExist || !isTargetExist)
			{
				retryWatch(!isSourceExist, !isTargetExist);
				return;
			}

			// Set diff check timer
			if (opInst.DiffCheckIntervalSec <= 0)
			{
				_checkDiffAllTimer.Stop();
			}
			else
			{
				_checkDiffAllTimer.Interval = opInst.DiffCheckIntervalSec * 1000;
			}

			// Stop if it's currently retrying
			_retryWatchTimer.Stop();
			_retryingCount = 0;

			// Source path watcher
			_sourceDirWatcher = new FileSystemWatcher();
			_sourceDirWatcher.Path = Path.GetDirectoryName(SourcePath) ?? string.Empty;
			_sourceDirWatcher.Filter = Path.GetFileName(SourcePath);
			_sourceDirWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName;
			_sourceDirWatcher.Deleted += onSourceRootChanged;
			_sourceDirWatcher.Renamed += onSourceRootChanged;
			_sourceDirWatcher.EnableRaisingEvents = true;
			_sourceDirWatcher.InternalBufferSize = Global.WATCHER_MAX_BUFFER_SIZE;

			// Target path watcher
			_targetDirWatcher = new FileSystemWatcher();
			_targetDirWatcher.Path = Path.GetDirectoryName(TargetPath) ?? string.Empty;
			_targetDirWatcher.Filter = Path.GetFileName(TargetPath);
			_targetDirWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName;
			_targetDirWatcher.Deleted += onTargetRootChanged;
			_targetDirWatcher.Renamed += onTargetRootChanged;
			_targetDirWatcher.EnableRaisingEvents = true;
			_targetDirWatcher.InternalBufferSize = Global.WATCHER_MAX_BUFFER_SIZE;

			// Create and initialize watcher
			_sourceWatcher = new FileSystemWatcher();
			_sourceWatcher.Path = SourcePath;
			_sourceWatcher.IncludeSubdirectories = true;
			_sourceWatcher.InternalBufferSize = Global.WATCHER_MAX_BUFFER_SIZE;
			_sourceWatcher.NotifyFilter =
				NotifyFilters.FileName |
				NotifyFilters.DirectoryName |
				NotifyFilters.Size |
				NotifyFilters.LastWrite;

			// Bind watching events
			_sourceWatcher.Created += onFileNotify;
			_sourceWatcher.Changed += onFileNotify;
			_sourceWatcher.Deleted += onFileNotify;
			_sourceWatcher.Renamed += onRenamed;
			_sourceWatcher.Disposed += onDisposed;
			_sourceWatcher.Error += onError;

			// Start watching
			_sourceWatcher.EnableRaisingEvents = true;

			IsEnabled = true;

			var evt = createBaseSyncEvent();
			evt.WatchType = SyncWatchType.CheckAll;
			_onFileStateChanged?.Invoke(evt);
		}

		public void Dispose()
		{
			if (!IsEnabled)
				return;

			if (_sourceDirWatcher != null)
			{
				_sourceDirWatcher.Deleted += onSourceRootChanged;
				_sourceDirWatcher.Renamed += onSourceRootChanged;
				_sourceDirWatcher.Error += onSourceRootError;
				_sourceDirWatcher.Dispose();
				_sourceDirWatcher = null;
			}

			if (_targetDirWatcher != null)
			{
				_targetDirWatcher.Deleted += onTargetRootChanged;
				_targetDirWatcher.Renamed += onTargetRootChanged;
				_targetDirWatcher.Error += onTargetRootError;
				_targetDirWatcher.Dispose();
				_targetDirWatcher = null;
			}

			if (_sourceWatcher != null)
			{
				_sourceWatcher.Created -= onFileNotify;
				_sourceWatcher.Changed -= onFileNotify;
				_sourceWatcher.Deleted -= onFileNotify;
				_sourceWatcher.Renamed -= onRenamed;
				_sourceWatcher.Disposed -= onDisposed;
				_sourceWatcher.Error -= onError;
				_sourceWatcher.Dispose();
				_sourceWatcher = null;
			}

			// Stop timer
			_retryWatchTimer.Stop();
			_retryingCount = 0;

			IsEnabled = false;
		}

		private void onFileNotify(object sender, FileSystemEventArgs e)
		{
			if (!IsEnabled)
			{
				Log.Warning($"[{SyncID}] OnFileNotify event occurred, but it was ignored because it's disabled.");
				return;
			}

			if (Option == null)
			{
				Log.Error($"[{SyncID}] Sync watcher doesn't has option!");
				return; // Wrong operation
			}

			FileInfo info = new FileInfo(e.FullPath);
			bool isDirectory = info.Attributes == FileAttributes.Directory;

			if (info.Attributes < 0)
			{
				// If it's deleted, determine whether it was a file or a directory based on the target.
				string target = Path.Combine(TargetPath, Path.GetRelativePath(SourcePath, e.FullPath));
				FileInfo tarInfo = new FileInfo(target);
				if (!tarInfo.Exists || tarInfo.Attributes < 0)
				{
					string extension = Path.GetExtension(e.FullPath);
					isDirectory = string.IsNullOrEmpty(extension);
				}
				else
				{
					isDirectory = tarInfo.Attributes == FileAttributes.Directory;
				}
			}

			var option = Option.Value;
			if (!Global.Filter.CheckFileter(e.FullPath, isDirectory, ref option))
				return;

			var evt = createBaseSyncEvent();
			evt.IsDirection = isDirectory;
			evt.SourceFullPath = e.FullPath;

			switch (e.ChangeType)
			{
				case WatcherChangeTypes.Created:
					evt.WatchType = SyncWatchType.Created;
					break;
				case WatcherChangeTypes.Deleted:
					evt.WatchType = SyncWatchType.Deleted;
					break;
				case WatcherChangeTypes.Changed:
					if (isDirectory)
						return;
					evt.WatchType = SyncWatchType.Modified;
					break;
				default:
					// Wrong operation
					Log.Error($"[{SyncID}] Cannot handle such type : {e.ChangeType}");
					break;
			}

			_onFileStateChanged?.Invoke(evt);
		}

		private void onRenamed(object sender, RenamedEventArgs e)
		{
			if (!IsEnabled)
			{
				Log.Warning($"[{SyncID}] OnFileNotify event occurred, but it was ignored because it's disabled.");
				return;
			}

			if (Option == null)
			{
				Log.Error($"[{SyncID}] Sync watcher doesn't has option!");
				return; // Wrong operation
			}

			bool isDirectory = !Path.HasExtension(e.FullPath);

			if (isDirectory)
			{
				foreach (string exDir in Option.Value.ExcludeDirectories)
				{
					string ext = exDir.ToLower();
					if (e.FullPath.Contains(ext) || e.OldFullPath.Contains(ext))
						return;
				}
			}
			else
			{
				var filters = Option.Value.IncludeExtensionFilters;
				bool isMatched = false;
				for (int i = 0; i < filters.Count; i++)
				{
					string filter = filters[i];

					string srcExtension = Path.GetExtension(e.FullPath).ToLower();
					string oldExtension = Path.GetExtension(e.OldFullPath).ToLower();

					if (string.Equals(oldExtension, filter))
					{
						if (srcExtension == ".tmp")
						{
							var modifiedEvent = createBaseSyncEvent();
							modifiedEvent.WatchType = SyncWatchType.Modified;
							modifiedEvent.SourceFullPath = e.OldFullPath;
							_onFileStateChanged?.Invoke(modifiedEvent);
							return;
						}
					}
					else if (string.Equals(srcExtension, filter))
					{
						isMatched = true;
						break;
					}
				}

				if (!isMatched)
					return; // Ignore if it's not matched
			}

			var evt = createBaseSyncEvent();
			evt.WatchType = SyncWatchType.Renamed;
			evt.IsDirection = isDirectory;
			evt.SourceFullPath = e.FullPath;
			evt.OldFileFullPath = e.OldFullPath;

			_onFileStateChanged?.Invoke(evt);
		}

		private void onDisposed(object? sender, EventArgs e)
		{
			Log.Information($"[{SyncID}] OnDisposed!");

			var evt = createBaseSyncEvent();
			evt.WatchType = SyncWatchType.Disposed;
			_onFileStateChanged?.Invoke(evt);
		}

		private void onError(object sender, ErrorEventArgs e)
		{
			if (!IsEnabled)
			{
				Log.Warning($"[{SyncID}] OnFileNotify event occurred, but it was ignored because it's disabled.");
				return;
			}

			Exception exception = e.GetException();
			Log.Error($"[{SyncID}] {exception.Message}");

			var evt = createBaseSyncEvent();
			evt.WatchType = SyncWatchType.Error;
			evt.ErrorMessage = exception.Message;
			_onFileStateChanged?.Invoke(evt);

			retryWatch(isException: true);
		}

		private void onSourceRootChanged(object sender, FileSystemEventArgs e)
			=> retryWatch(isSourceMissing: true);
		private void onSourceRootError(object sender, ErrorEventArgs e)
			=> retryWatch(isSourceMissing: true);

		private void onTargetRootChanged(object sender, FileSystemEventArgs e)
			=> retryWatch(isTargetMissing: true);
		private void onTargetRootError(object sender, ErrorEventArgs e)
			=> retryWatch(isTargetMissing: true);

		private void retryWatch(bool isSourceMissing = false,
								bool isTargetMissing = false,
								bool isException = false,
								Exception? exception = null)
		{
			if (IsRetrying)
			{
				return;
			}

			if (isSourceMissing)
				Log.Warning($"[{SyncID}] Source path was missing! Path : {SourcePath}");

			if (isTargetMissing)
				Log.Warning($"[{SyncID}] Target path was missing! Path : {TargetPath}");

			if (isException && exception != null)
			{
				Log.Error($"[{SyncID}] An exception occur! {exception.Message}");
			}

			if (!Option.HasValue)
				return;

			Dispose();
			_retryingCount = Option.Value.RetryCount + 1;
			_retryWatchTimer.Start();
		}

		private void onRetryWatch(object? sender, System.Timers.ElapsedEventArgs e)
		{
			if (!Option.HasValue)
			{
				Log.Error($"[{SyncID}] There is no option!");
				return;
			}

			_retryingCount--;
			if (_retryingCount <= 0)
			{
				_retryingCount = 0;
				_retryWatchTimer.Stop();
				return;
			}

			Log.Warning($"[{SyncID}] Retrying sync...");
			Start(Option.Value);
		}

		private void onCheckDiffAll(object? sender, System.Timers.ElapsedEventArgs e)
		{
			if (!Option.HasValue)
			{
				Log.Error($"[{SyncID}] There is no option!");
				return;
			}

			Log.Information($"[{SyncID}] Check all files...");

			var evt = createBaseSyncEvent();
			evt.WatchType = SyncWatchType.CheckAll;
			_onFileStateChanged?.Invoke(evt);
		}

		private SyncWatchEvents createBaseSyncEvent()
		{
			SyncWatchEvents evt = new SyncWatchEvents()
			{
				SourcePath = SourcePath,
				TargetPath = TargetPath,
			};
			return evt;
		}
	}
}
