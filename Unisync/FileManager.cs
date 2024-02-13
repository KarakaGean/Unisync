#define CUSTOM_LOG
//#define FILTER

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace Unisync
{
	public static class FileManager
	{
		public static bool TryCopyDirectory(string srcDirPath,
											string destDirPath,
#if CUSTOM_LOG
											Action<string>? warnOutput,
#endif
											Func<string, string, bool>? shouldCopyFile,
											[NotNullWhen(false)]
											out Exception? ex)
		{
			if (!Directory.Exists(srcDirPath))
			{
				ex = new Exception($"There is no directory at : {srcDirPath}");
				return false;
			}

			Queue<string> pathQeue = new Queue<string>(64);
			pathQeue.Enqueue(srcDirPath);

			while (pathQeue.TryDequeue(out string? curPath))
			{
				if (!Directory.Exists(curPath))
				{
#if CUSTOM_LOG
					warnOutput?.Invoke($"There is no directroy at : {curPath}");
#endif
					continue;
				}

				foreach (var dir in Directory.GetDirectories(curPath))
				{
					pathQeue.Enqueue(dir);
				}

				foreach (var curFilePath in Directory.GetFiles(curPath))
				{
					FileInfo fileInfo = new FileInfo(curFilePath);
					if (!fileInfo.Exists || fileInfo.Attributes == FileAttributes.Directory)
					{
#if CUSTOM_LOG
						warnOutput?.Invoke($"The file was deleted or moved during copy process!\nFile : {fileInfo.FullName}");
#endif
						continue;
					}

					string relativePath = Path.GetRelativePath(srcDirPath, curFilePath);
					string destFilePath = Path.Combine(destDirPath, relativePath);

					if (shouldCopyFile != null && shouldCopyFile(curFilePath, destFilePath) == false)
					{
						// Ignore file by condition
						continue;
					}

					if (!TryCopyFile(curFilePath, destFilePath, warnOutput, out ex))
					{
#if CUSTOM_LOG
						warnOutput?.Invoke($"Error occur during copy file!\nources : {curFilePath}\nTarget : {destFilePath}\nException : {ex.Message}");
#endif
						continue;
					}
				}
			}

			ex = null;
			return true;
		}

		public static bool TryCopyFile(string srcFilePath,
									   string destFilePath,
#if CUSTOM_LOG
									   Action<string>? warnOutput,
#endif
									   [NotNullWhen(false)]
									   out Exception? ex)
		{
			FileInfo fileInfo = new FileInfo(srcFilePath);
			if (fileInfo.Attributes == FileAttributes.Directory)
			{
				ex = new Exception($"The file path is actualy a directory!\nPath : {srcFilePath}");
				return false;
			}

			if (!fileInfo.Exists)
			{
				ex = new Exception($"There is no file!\nPath : {srcFilePath}");
				return false;
			}

			try
			{
				MakeDirectoryIfNotExist(destFilePath);
				File.Copy(srcFilePath, destFilePath, overwrite: true);
			}
			catch (Exception exception)
			{
				ex = exception;
				return false;
			}

			ex = null;
			return true;
		}

		public static void MakeDirectoryIfNotExist(string path)
		{
			string? dirPath = Path.GetDirectoryName(path);
			if (string.IsNullOrWhiteSpace(dirPath))
			{
				throw new Exception("IT'S ROOT DIRECTORY OR EMPTY!");
			}

			if (!Directory.Exists(dirPath))
			{
				Directory.CreateDirectory(dirPath);
			}
		}

		/// <summary>
		/// Fast compare over 10mb size files
		/// </summary>
		/// <param name="fileA"></param>
		/// <param name="fileB"></param>
		/// <returns></returns>
		public static bool CompareBigFiles(string fileA, string fileB)
		{
			// Determine if the same file was referenced two times.
			if (string.Equals(fileA, fileB))
			{
				return true;
			}

			using (FileStream fsa = new FileStream(fileA, FileMode.Open))
			using (FileStream fsb = new FileStream(fileB, FileMode.Open))
			{
				long la = fsa.Length;
				long lb = fsb.Length;

				// Check the file sizes. If they are not the same, the files are not the same.
				if (la != lb)
					return false;

				int readPos = 0;

				int READ_STRIDE = 1024 * 64;

				Span<byte> buffA = stackalloc byte[READ_STRIDE];
				Span<byte> buffB = stackalloc byte[READ_STRIDE];
				buffA.Clear();
				buffB.Clear();
				int readA = 0;
				int readB = 0;

				while (readPos < la)
				{
					fsa.ReadAtLeast(buffA, READ_STRIDE, throwOnEndOfStream: false);
					fsb.ReadAtLeast(buffB, READ_STRIDE, throwOnEndOfStream: false);

					for (int i = 0; i < READ_STRIDE; i += 1)
					{
						readA = buffA[i];
						readB = buffB[i];

						if (readA != readB)
							return false;
					}
					readPos += READ_STRIDE;
				}

				return true;
			}
		}

		public static bool CompareSmallFiles(string fileA, string fileB)
		{
			FileInfo a = new FileInfo(fileA);
			FileInfo b = new FileInfo(fileB);

			const int BYTES_TO_READ = sizeof(Int64);

			if (a.Length != b.Length)
				return false;

			if (string.Equals(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase))
				return true;

			int iterations = (int)Math.Ceiling((double)a.Length / BYTES_TO_READ);

			using (FileStream fs1 = a.OpenRead())
			using (FileStream fs2 = b.OpenRead())
			{
				byte[] one = new byte[BYTES_TO_READ];
				byte[] two = new byte[BYTES_TO_READ];

				for (int i = 0; i < iterations; i++)
				{
					fs1.Read(one, 0, BYTES_TO_READ);
					fs2.Read(two, 0, BYTES_TO_READ);

					if (BitConverter.ToInt64(one, 0) != BitConverter.ToInt64(two, 0))
						return false;
				}
			}

			return true;
		}

		public static bool CheckIsValid(string path)
		{
			return !string.IsNullOrWhiteSpace(Path.GetDirectoryName(path));
		}
	}
}
