using System.Diagnostics;
using System.Text;

/// <summary>
/// Application for extracting the songs from the "BeMusicSeeker difficulty tables BMS PACK" and
/// "BeMusicSeeker difficulty tables BMS PACK APPEND" torrents. This will search a given input
/// directory for folders matching these names:
///   BeMusicSeeker difficulty tables BMS PACK...
///   BeMusicSeeker difficulty tables BMS PACK APPEND...
/// It will unzip their songs to two new folders within the specified output directory:
///   Base
///   Append
/// It is then expected to run add-extract-to-songs-folder to merge these into an existing songs
/// folder that is organized by title.
/// </summary>
internal sealed class Program
{
	private const string SevenZip = "C:\\Program Files\\7-Zip\\7z.exe";
	private static string InputDirectory;
	private static string OutputDirectory;
	private static string OutputBaseDirectory = "Base";
	private static string OutputAppendDirectory = "Append";

	private class ArchiveTaskData
	{
		public readonly FileInfo Archive;
		public readonly string OutputDir;

		private readonly Func<Task> TaskFunc;
		private readonly long ArchiveSize;
		private Task Task;

		public ArchiveTaskData(FileInfo archive, string outputDir)
		{
			Archive = archive;
			OutputDir = outputDir;
			ArchiveSize = Archive.Length;
			TaskFunc = async () => await ProcessArchive(this);
		}

		public void Start()
		{
			Task = TaskFunc();
		}

		public bool IsDone()
		{
			return Task?.IsCompleted ?? false;
		}

		public long GetSize()
		{
			return ArchiveSize;
		}
	}

	private class ArchiveTaskDataComparer : IComparer<ArchiveTaskData>
	{
		public int Compare(ArchiveTaskData t1, ArchiveTaskData t2)
		{
			if (ReferenceEquals(t1, t2))
				return 0;
			return string.Compare(t1.Archive.Name, t2.Archive.Name, StringComparison.Ordinal);
		}
	}

	private static void Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;

		ParseArgs(args);
		SetupOutputDirectories();
		var (allBaseFiles, allUpdates) = GatherArchives();
		ProcessArchives(allBaseFiles);
		ProcessArchives(allUpdates);
	}

	private static void ParseArgs(string[] args)
	{
		if (args == null || args.Length != 2)
		{
			PrintUsage();
			Environment.Exit(1);
		}

		InputDirectory = args[0];
		OutputDirectory = args[1];
	}

	private static void PrintUsage()
	{
		Console.WriteLine(
			"Usage: extract-be-music-seeker input-directory output-directory"
			+ "\nExample: extract-be-music-seeker C:\\Users\\bms\\Downloads C:\\Users\\bms\\Downloads\\be-music-seeker-output");
	}

	private static void SetupOutputDirectories()
	{
		var baseDir = Path.Combine(OutputDirectory, OutputBaseDirectory);
		var appendDir = Path.Combine(OutputDirectory, OutputAppendDirectory);
		try
		{
			Directory.Delete(baseDir, true);
			Directory.Delete(appendDir, true);
		}
		catch (Exception)
		{
			// Ignored.
		}

		Directory.CreateDirectory(Path.Combine(OutputDirectory, OutputBaseDirectory));
		Directory.CreateDirectory(Path.Combine(OutputDirectory, OutputAppendDirectory));
	}

	private static (List<ArchiveTaskData> allBaseFiles, List<ArchiveTaskData> allUpdates) GatherArchives()
	{
		var allUpdates = new List<ArchiveTaskData>();
		var allBaseFiles = new List<ArchiveTaskData>();
		Console.WriteLine($"Gathering archives from {InputDirectory}...");

		try
		{
			var allDirsInInputDir = Directory.GetDirectories(InputDirectory);
			foreach (var inputDirName in allDirsInInputDir)
			{
				var di = new DirectoryInfo(inputDirName);
				if (!di.Name.StartsWith("BeMusicSeeker difficulty tables BMS PACK"))
					continue;
				var isAppend = di.Name.StartsWith("BeMusicSeeker difficulty tables BMS PACK APPEND");
				var outputDir = Path.Combine(OutputDirectory, isAppend ? OutputAppendDirectory : OutputBaseDirectory);

				var allFiles = Directory.GetFiles(inputDirName);
				Array.Sort(allFiles, (a, b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase));
				if (allFiles.Length == 1 && allFiles[0].EndsWith("update.rar"))
				{
					allUpdates.Add(new ArchiveTaskData(new FileInfo(allFiles[0]), outputDir));

					var subDirectories = Directory.GetDirectories(inputDirName);
					if (subDirectories.Length == 1 && subDirectories[0].EndsWith("newsongs"))
					{
						allFiles = Directory.GetFiles(subDirectories[0]);
						Array.Sort(allFiles, (a, b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase));
					}
				}

				foreach (var file in allFiles)
				{
					if (!file.EndsWith(".rar"))
						continue;

					var fi = new FileInfo(file);
					allBaseFiles.Add(new ArchiveTaskData(fi, outputDir));
				}
			}

			allBaseFiles.Sort(new ArchiveTaskDataComparer());

			// Print num files and size.
			var totalSize = 0L;
			foreach (var file in allBaseFiles)
				totalSize += file.GetSize();
			var totalGb = totalSize / (1024 * 1024 * 1024);

			var totalUpdateSize = 0L;
			foreach (var file in allUpdates)
				totalUpdateSize += file.GetSize();
			var totalUpdateGb = totalUpdateSize / (1024 * 1024 * 1024);

			Console.WriteLine(
				$"Found {allBaseFiles.Count} ({totalGb}GB) song archives and {allUpdates.Count} ({totalUpdateGb}GB) update archives to process.");
		}
		catch (Exception e)
		{
			Console.WriteLine($"Failed to gather archives: {e}");
			Environment.Exit(1);
		}

		return (allBaseFiles, allUpdates);
	}

	private static void ProcessArchives(IReadOnlyList<ArchiveTaskData> archiveTasks)
	{
		var totalProcessedBytes = 0L;
		var pendingTasks = new List<ArchiveTaskData>();
		var totalArchiveBytes = 0L;
		foreach (var archiveTask in archiveTasks)
		{
			pendingTasks.Add(archiveTask);
			totalArchiveBytes += archiveTask.GetSize();
		}

		var inProgressTasks = new List<ArchiveTaskData>();
		var totalArchiveCount = archiveTasks.Count;
		var lastKnownRemainingCount = totalArchiveCount;

		var concurrentTaskCount = Environment.ProcessorCount;

		// Process all song tasks.
		var stopWatch = new Stopwatch();
		stopWatch.Start();
		while (pendingTasks.Count > 0 || inProgressTasks.Count > 0)
		{
			// See if any in progress tasks are now done.
			var numTasksToAdd = concurrentTaskCount;
			var tasksToRemove = new List<ArchiveTaskData>();
			foreach (var inProgressTask in inProgressTasks)
			{
				if (inProgressTask.IsDone())
				{
					totalProcessedBytes += inProgressTask.GetSize();
					tasksToRemove.Add(inProgressTask);
				}
				else
				{
					numTasksToAdd--;
				}
			}

			// Remove completed tasks.
			foreach (var taskToRemove in tasksToRemove)
				inProgressTasks.Remove(taskToRemove);
			tasksToRemove.Clear();

			// Add more tasks.
			var numTasksStarted = 0;
			while (numTasksToAdd > 0 && pendingTasks.Count > 0)
			{
				var taskToStart = pendingTasks[0];
				inProgressTasks.Add(taskToStart);
				pendingTasks.RemoveAt(0);
				taskToStart.Start();
				numTasksToAdd--;
				numTasksStarted++;
			}

			// If we have completed more tasks this loop, log a progress update.
			if (lastKnownRemainingCount != pendingTasks.Count + inProgressTasks.Count)
			{
				lastKnownRemainingCount = pendingTasks.Count + inProgressTasks.Count;
				var processedCount = totalArchiveCount - lastKnownRemainingCount;
				var taskPercent = 100.0 * ((double)processedCount / totalArchiveCount);
				var bytePercent = 100.0 * ((double)totalProcessedBytes / totalArchiveBytes);
				var totalProcessedMb = totalProcessedBytes / 1024.0 / 1024.0;
				var totalSongMb = totalArchiveBytes / 1024.0 / 1024.0;
				Console.WriteLine(
					$"Progress: {taskPercent:F2}%. {processedCount}/{totalArchiveCount} archives. {totalProcessedMb:F2}/{totalSongMb:F2} MB ({bytePercent:F2}%).");
			}

			// If we added a lot of tasks then it means we are processing quickly and can speed up.
			// If we added a small number of tasks then it means we are processing slowly and can wait.
			var sleepTime = (int)Lerp(100, 10, 0, concurrentTaskCount, numTasksStarted);
			Thread.Sleep(sleepTime);
		}

		stopWatch.Stop();
		Console.WriteLine($"Processed {totalArchiveCount} archives in {stopWatch.Elapsed}.");
	}

	private static async Task ProcessArchive(ArchiveTaskData data)
	{
		var process = new Process();
		process.StartInfo.FileName = SevenZip;
		process.StartInfo.RedirectStandardOutput = true;
		process.StartInfo.Arguments = $"x \"{data.Archive.FullName}\" -aoa -y -o\"{data.OutputDir}\"";
		process.Start();
		await process.WaitForExitAsync();
		if (process.ExitCode != 0)
		{
			Console.WriteLine($"Extracting \"{data.Archive.FullName}\" failed.");
		}
	}

	public static float Lerp(float startValue, float endValue, int startTime, int endTime, int currentTime)
	{
		if (endTime == startTime)
			return currentTime >= endTime ? endValue : startValue;
		var ret = startValue + (float)(currentTime - startTime) / (endTime - startTime) * (endValue - startValue);
		if (startValue < endValue)
			return Clamp(ret, startValue, endValue);
		return Clamp(ret, endValue, startValue);
	}

	public static float Clamp(float value, float min, float max)
	{
		return value < min ? min : value > max ? max : value;
	}
}
