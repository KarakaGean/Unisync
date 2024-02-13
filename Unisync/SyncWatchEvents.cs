using System.Text;

namespace Unisync
{
	public struct SyncWatchEvents
	{
		public SyncWatchType WatchType;
		public bool IsDirection;

		/// <summary>동기화 원본 디렉토리 위치입니다.</summary>
		public string SourcePath = string.Empty;
		/// <summary>동기화 대상 디렉토리 위치입니다.</summary>
		public string TargetPath = string.Empty;

		public string SourceFullPath = string.Empty;
		public string OldFileFullPath = string.Empty;

		public string ErrorMessage = string.Empty;

		public string TargetFullPath
		{
			get
			{
				return Path.Combine(TargetPath, Path.GetRelativePath(SourcePath, SourceFullPath));
			}
		}
		public string TargetOldFullPath
		{
			get
			{
				return Path.Combine(TargetPath, Path.GetRelativePath(SourcePath, OldFileFullPath));
			}
		}

		public SyncWatchEvents()
		{
		}

		public bool IsValidPath()
		{
			bool result = true;

			switch (WatchType)
			{
				case SyncWatchType.CheckAll:
					result |= FileManager.CheckIsValid(SourcePath);
					result |= FileManager.CheckIsValid(TargetPath);
					break;
				case SyncWatchType.Created:
				case SyncWatchType.Modified:
				case SyncWatchType.Deleted:
					result |= FileManager.CheckIsValid(SourceFullPath);
					break;
				case SyncWatchType.Renamed:
					result |= FileManager.CheckIsValid(SourceFullPath);
					result |= FileManager.CheckIsValid(OldFileFullPath);
					break;
				case SyncWatchType.Disposed:
				case SyncWatchType.Error:
				case SyncWatchType.Error_SourcePath:
				case SyncWatchType.Error_TargetPath:
					break;
			}

			return result;
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(IsDirection ? "[Dir]" : "[File]");
			sb.Append($"({nameof(WatchType)}:{WatchType})");

			switch (WatchType)
			{
				case SyncWatchType.CheckAll:
					sb.Append($"({nameof(SourcePath)}:{SourcePath})({nameof(TargetPath)}:{TargetPath})");
					break;
				case SyncWatchType.Created:
				case SyncWatchType.Modified:
				case SyncWatchType.Deleted:
					sb.Append($"({nameof(SourceFullPath)}:{SourceFullPath})");
					break;
				case SyncWatchType.Renamed:
					sb.Append($"({nameof(SourceFullPath)}:{SourceFullPath})({nameof(OldFileFullPath)}:{OldFileFullPath})");
					break;
				case SyncWatchType.Disposed:
					break;
				case SyncWatchType.Error:
					sb.Append($"({nameof(ErrorMessage)}:{ErrorMessage})");
					break;
				case SyncWatchType.Error_SourcePath:
					sb.Append($"({nameof(ErrorMessage)}:{ErrorMessage})({nameof(SourcePath)}:{SourcePath})");
					break;
				case SyncWatchType.Error_TargetPath:
					sb.Append($"({nameof(ErrorMessage)}:{ErrorMessage})({nameof(TargetPath)}:{TargetPath})");
					break;
			}

			return sb.ToString();
		}
	}
}
