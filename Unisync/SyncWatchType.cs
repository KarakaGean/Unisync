namespace Unisync
{
	public enum SyncWatchType
	{
		None = 0,
		CheckAll,
		Created,
		Modified,
		Deleted,
		Renamed,
		Disposed,
		Error,
		Error_SourcePath,
		Error_TargetPath,
	}
}
