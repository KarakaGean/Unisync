namespace Unisync
{
	public struct SyncOption
	{
		public string Group { get; set; } = string.Empty;

		public string Tag { get; set; } = string.Empty;

		public string SourcePath { get; set; } = string.Empty;

		public string TargetPath { get; set; } = string.Empty;

		public List<string> IncludeExtensionFilters { get; set; } = new();

		public List<string> ExcludeDirectories { get; set; } = new();

		/// <summary>
		/// Periodically check for file differences. If different, synchronize the files.
		/// It's in seconds. If the value is 0, do not check periodically.
		/// </summary>
		public int DiffCheckIntervalSec { get; set; } = 0;

		/// <summary>
		/// 파일 감지에 실패했을 때 재시도하는 Interval입니다. 초 단위입니다.
		/// </summary>
		public int RetryIntervalSec { get; set; } = 2;

		/// <summary>
		/// 파일 감지에 실패했을 때의 재시도 횟수입니다.
		/// </summary>
		public int RetryCount { get; set; } = 10;

		///// <summary>
		///// 대상 폴더의 이동이나 이름 변경을 추적하고, 설정에 기록합니다.
		///// </summary>
		//public bool AllowTargetPathChange { get; set; } = true;

		///// <summary>
		///// 원본 폴더의 이동이나 이름 변경을 추적하고, 설정에 기록합니다.
		///// </summary>
		//public bool AllowSourcePathChange { get; set; } = true;

		public SyncOption()
		{
		}
	}
}
