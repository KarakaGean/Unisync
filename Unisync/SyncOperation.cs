namespace Unisync
{
	public struct SyncOperation
	{
		public SyncWatchType Type;
		public string SourcePath;
		public string TargetPath;
		public bool IsSourceToTarget;

		public SyncOperation(SyncWatchType type,
							 string sourcePath, 
							 string targetPath, 
							 bool isSourceToTarget)
		{
			Type = type;
			SourcePath = sourcePath;
			TargetPath = targetPath;
			IsSourceToTarget = isSourceToTarget;
		}
	}

}
