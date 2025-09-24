using System.Text;

/// <summary>
/// Takes songs from a given input directory and merges them into a given existing songs folder organized by name.
/// Does its best at determining which files to keep when conflicts arise.
/// Running this is destructive and will move/delete songs from the input directory.
/// </summary>
internal sealed class Program
{
	private static string InputDirectory;
	private static string OutputDirectory;
	private static string InputPackNameForConflicts;
	private static Dictionary<char, DirectoryInfo> OutputAlphabetical;
	private static DirectoryInfo OutputNumbers;
	private static DirectoryInfo OutputOthers;
	private static DirectoryInfo OutputKatakana;
	private static DirectoryInfo OutputHiragana;
	private static DirectoryInfo OutputKanji;

	private static void Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;

		ParseArgs(args);
		MakeSongFolders();
		MoveFiles();
		Console.WriteLine("Done");
	}

	private static void ParseArgs(string[] args)
	{
		if (args == null || !(args.Length == 2 || args.Length == 3))
		{
			PrintUsage();
			Environment.Exit(1);
		}

		InputDirectory = args[0];
		OutputDirectory = args[1];
		if (args.Length > 2)
			InputPackNameForConflicts = args[2];
	}

	private static void PrintUsage()
	{
		Console.WriteLine(
			"Usage: add-extracted-to-songs-folder input-directory output-directory <pack-name-for-conflicts>"
			+ "\n<pack-name-for-conflicts> will be reformatted to \" (<pack-name-for-conflicts>)\" for file names."
			+ "\nExample: add-extracted-to-songs-folder C:\\Users\\bms\\Downloads\\be-music-seeker-output\\Append C:\\Users\\bms\\songs Append");
	}

	private static void MakeSongFolders()
	{
		OutputAlphabetical = new Dictionary<char, DirectoryInfo>();
		for (var c = 'A'; c <= 'Z'; c++)
			OutputAlphabetical[c] = Directory.CreateDirectory(Path.Combine(OutputDirectory, $"[Title] {c.ToString()}"));
		OutputNumbers = Directory.CreateDirectory(Path.Combine(OutputDirectory, "[Title] 0-9"));
		OutputOthers = Directory.CreateDirectory(Path.Combine(OutputDirectory, "[Title] _Others"));
		OutputKatakana = Directory.CreateDirectory(Path.Combine(OutputDirectory, "[Title] カタカナ"));
		OutputHiragana = Directory.CreateDirectory(Path.Combine(OutputDirectory, "[Title] ひらがな"));
		OutputKanji = Directory.CreateDirectory(Path.Combine(OutputDirectory, "[Title] 漢字"));
	}

	private static void MoveFiles()
	{
		var allDirsInInputDir = Directory.GetDirectories(InputDirectory);
		foreach (var songFolder in allDirsInInputDir)
		{
			try
			{
				var di = new DirectoryInfo(songFolder);
				var outputDirectory = GetDirectoryForSongFolder(di);

				string desiredOutputSongFolder = null;

				// Check the conflict-specific folder first. If this exists, we should compare against it and not the actual folder name.
				if (!string.IsNullOrEmpty(InputPackNameForConflicts))
				{
					var conflictSpecificDestSongFolder =
						Path.Combine(outputDirectory.FullName, $"{di.Name} ({InputPackNameForConflicts})");
					if (Directory.Exists(conflictSpecificDestSongFolder))
						desiredOutputSongFolder = conflictSpecificDestSongFolder;
				}

				// If we didn't find a conflict-specific folder, use the default normal folder for comparing.
				desiredOutputSongFolder ??= Path.Combine(outputDirectory.FullName, di.Name);

				// Simple case, no existing file is present.
				if (!Directory.Exists(desiredOutputSongFolder))
				{
					Directory.Move(songFolder, desiredOutputSongFolder);
					Console.WriteLine($"\"{di.Name}\" -> \"{outputDirectory.Name}{Path.DirectorySeparatorChar}{di.Name}\"");
					continue;
				}

				// The destination already has a folder for this song. Try to handle the conflict.
				HandleConflict(di, new DirectoryInfo(desiredOutputSongFolder));
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to move {songFolder}: {e}");
			}
		}
	}

	private class DirectoryComparison
	{
		public int NumUniqueSourceFiles;
		public int NumUniqueDestFiles;
		public int NumFilesInBothSourceAndDest;
		public int NumFilesInBothThatAreNotIdentical;
		public int NumFilesInSource;
		public int NumFilesInDest;
		public int NumUniqueSourceSubDirectories;
		public int NumUniqueDestSubDirectories;
		public int NumSubDirectoriesInBothSourceAndDest;
		public int NumLargerDivergentSourceFiles;
		public int NumLargerDivergentDestFiles;

		public void AddSubDirectoryComparision(DirectoryComparison subDirectoryComparison)
		{
			NumUniqueSourceFiles += subDirectoryComparison.NumUniqueSourceFiles;
			NumUniqueDestFiles += subDirectoryComparison.NumUniqueDestFiles;
			NumFilesInBothSourceAndDest += subDirectoryComparison.NumFilesInBothSourceAndDest;
			NumFilesInBothThatAreNotIdentical += subDirectoryComparison.NumFilesInBothThatAreNotIdentical;
			NumFilesInSource += subDirectoryComparison.NumFilesInSource;
			NumFilesInDest += subDirectoryComparison.NumFilesInDest;
			NumUniqueSourceSubDirectories += subDirectoryComparison.NumUniqueSourceSubDirectories;
			NumUniqueDestSubDirectories += subDirectoryComparison.NumUniqueDestSubDirectories;
			NumSubDirectoriesInBothSourceAndDest += subDirectoryComparison.NumSubDirectoriesInBothSourceAndDest;
			NumLargerDivergentSourceFiles += subDirectoryComparison.NumLargerDivergentSourceFiles;
			NumLargerDivergentDestFiles += subDirectoryComparison.NumLargerDivergentDestFiles;
		}
	}

	private static void HandleConflict(DirectoryInfo source, DirectoryInfo dest)
	{
		var result = HandleConflictRecursive(source, dest);

		// Source has nothing in it. Delete it.
		if (result.NumFilesInSource == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0)
		{
			DeleteDirectory(source);
			Console.WriteLine($"\"{source.Name}\" -> Deleted. Source is empty.");
			return;
		}

		// Dest has nothing in it. Take source.
		if (result.NumFilesInDest == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0)
		{
			var fullDestPath = dest.FullName;
			var destParent = dest.Parent;
			var destNameForLogging =
				destParent != null ? $"{destParent.Name}{Path.DirectorySeparatorChar}{dest.Name}" : dest.Name;
			DeleteDirectory(dest);
			Directory.Move(source.FullName, fullDestPath);

			Console.WriteLine($"\"{source.Name}\" -> \"{destNameForLogging}\". Dest was empty.");
			return;
		}

		// The directories are the same. Don't copy.
		if (result.NumUniqueSourceFiles == 0
		    && result.NumUniqueDestFiles == 0
		    && result.NumFilesInBothThatAreNotIdentical == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0)
		{
			DeleteDirectory(source);
			Console.WriteLine($"\"{source.Name}\" -> Deleted. Identical to existing output folder.");
			return;
		}

		// The only difference is unique files already in the destination. Don't copy.
		if ((result.NumUniqueDestFiles > 0 || result.NumUniqueDestSubDirectories > 0)
		    && result.NumUniqueSourceFiles == 0
		    && result.NumFilesInBothThatAreNotIdentical == 0
		    && result.NumUniqueSourceSubDirectories == 0)
		{
			DeleteDirectory(source);
			Console.WriteLine($"\"{source.Name}\" -> Deleted. Only difference is unique files already in output folder.");
			return;
		}

		// The only difference is unique source files. Just copy those files.
		if ((result.NumUniqueSourceFiles > 0 || result.NumUniqueSourceSubDirectories > 0)
		    && result.NumUniqueDestFiles == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothThatAreNotIdentical == 0)
		{
			var fullDestPath = dest.FullName;
			var destParent = dest.Parent;
			var destNameForLogging =
				destParent != null ? $"{destParent.Name}{Path.DirectorySeparatorChar}{dest.Name}" : dest.Name;

			DeleteDirectory(dest);
			Directory.Move(source.FullName, fullDestPath);
			Console.WriteLine(
				$"\"{source.Name}\" -> \"{destNameForLogging}\". Deleted existing output before move. Only difference is unique source files.");
			return;
		}

		// If the source contains a small number of files, and they are all unique and there is no other overlap
		// it is likely the source is a patch, and we should copy the files over.
		if (result.NumFilesInBothSourceAndDest == 0
		    && result.NumFilesInBothThatAreNotIdentical == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueSourceFiles == result.NumFilesInSource
		    && result.NumUniqueDestFiles == result.NumFilesInDest
		    && result.NumFilesInSource > 0
		    && result.NumFilesInDest > 0
		    && result.NumFilesInSource < 0.05 * result.NumFilesInDest)
		{
			var sourceFiles = source.GetFiles();
			var numSourceFiles = sourceFiles.Length;
			foreach (var file in sourceFiles)
				File.Move(file.FullName, Path.Combine(dest.FullName, file.Name));
			DeleteDirectory(source);
			Console.WriteLine(
				$"\"{source.Name}\" -> Merged {numSourceFiles} files. Source only contained a small number of files and they were all unique.");
			return;
		}

		// Similar check to above. If source looks like a patch and contains some unique files, copy those files over.
		if (result.NumFilesInBothThatAreNotIdentical == 0
		    && result.NumFilesInSource > 0 && result.NumFilesInSource < 0.5 * result.NumFilesInDest
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueSourceFiles > 0)
		{
			var numMovedFiles = 0;
			foreach (var file in source.GetFiles())
			{
				var destFileName = Path.Combine(dest.FullName, file.Name);
				if (!File.Exists(destFileName))
				{
					File.Move(file.FullName, destFileName);
					numMovedFiles++;
				}
			}

			DeleteDirectory(source);

			Console.WriteLine(
				$"\"{source.Name}\" -> Merged {numMovedFiles} files. Source only contained a small number of files and there were no conflicts.");
			return;
		}

		// If the source and dest are largely identical with there only being a small number of unique files in both
		// the source and dest folders, just copy over the unique source folders.
		if (result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInBothThatAreNotIdentical == 0
		    && result.NumFilesInBothSourceAndDest > 0.95 * result.NumFilesInSource
		    && result.NumFilesInBothSourceAndDest > 0.95 * result.NumFilesInDest
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInSource > 0 && result.NumUniqueSourceFiles > 0 &&
		    result.NumUniqueSourceFiles < 0.05 * result.NumFilesInSource
		    && result.NumFilesInDest > 0 && result.NumUniqueDestFiles > 0 &&
		    result.NumUniqueDestFiles < 0.05 * result.NumFilesInDest)
		{
			var numMovedFiles = 0;
			foreach (var file in source.GetFiles())
			{
				var destFileName = Path.Combine(dest.FullName, file.Name);
				if (!File.Exists(destFileName))
				{
					File.Move(file.FullName, destFileName);
					numMovedFiles++;
				}
			}

			DeleteDirectory(source);

			Console.WriteLine(
				$"\"{source.Name}\" -> Merged {numMovedFiles} files. Source and dest contained no conflicts and each had a small number of unique files.");
			return;
		}

		// If there are no unique files in either source or dest and the only difference is actual file diffs,
		// and the number of files with diffs is small and the differing files are all larger in one folder,
		// then take the larger set because they are likely asset up-scaling or larger patch notes / readmes.
		if (result.NumUniqueSourceFiles == 0
		    && result.NumUniqueDestFiles == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInSource == result.NumFilesInBothSourceAndDest
		    && result.NumFilesInDest == result.NumFilesInBothSourceAndDest
		    && result.NumFilesInBothThatAreNotIdentical > 0
		    && result.NumFilesInBothThatAreNotIdentical < 0.05 * result.NumFilesInBothSourceAndDest
		    && (result.NumLargerDivergentSourceFiles == result.NumFilesInBothThatAreNotIdentical ||
		        result.NumLargerDivergentDestFiles == result.NumFilesInBothThatAreNotIdentical))
		{
			// All divergent source files are larger, take them.
			if (result.NumLargerDivergentSourceFiles == result.NumFilesInBothThatAreNotIdentical)
			{
				// It is simpler to just copy all the source.
				var fullDestPath = dest.FullName;
				var destParent = dest.Parent;
				var destNameForLogging =
					destParent != null ? $"{destParent.Name}{Path.DirectorySeparatorChar}{dest.Name}" : dest.Name;
				DeleteDirectory(dest);
				Directory.Move(source.FullName, fullDestPath);

				Console.WriteLine(
					$"\"{source.Name}\" -> \"{destNameForLogging}\". Source and dest were identical except for {result.NumFilesInBothThatAreNotIdentical} divergent files, all of which were larger in the source.");
			}

			// All divergent dest files are larger, keep them.
			else
			{
				DeleteDirectory(source);
				Console.WriteLine(
					$"\"{source.Name}\" -> Deleted. Source and dest were identical except for {result.NumFilesInBothThatAreNotIdentical} divergent files, all of which were larger in the destination.");
			}

			return;
		}

		// Similar to the above, but also allow a merge if there are only unique source files and the number is small, and the divergent
		// files are all larger in the source.
		if (result.NumUniqueSourceFiles > 0
		    && result.NumUniqueSourceFiles < 0.05 * result.NumFilesInSource
		    && result.NumUniqueDestFiles == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInSource > 0.95 * result.NumFilesInBothSourceAndDest
		    && result.NumFilesInDest == result.NumFilesInBothSourceAndDest
		    && result.NumFilesInBothThatAreNotIdentical > 0
		    && result.NumFilesInBothThatAreNotIdentical < 0.05 * result.NumFilesInBothSourceAndDest
		    && result.NumLargerDivergentSourceFiles == result.NumFilesInBothThatAreNotIdentical
		    && result.NumLargerDivergentDestFiles == 0)
		{
			// It is simpler to just copy all the source.
			var fullDestPath = dest.FullName;
			var destParent = dest.Parent;
			var destNameForLogging =
				destParent != null ? $"{destParent.Name}{Path.DirectorySeparatorChar}{dest.Name}" : dest.Name;
			DeleteDirectory(dest);
			Directory.Move(source.FullName, fullDestPath);

			Console.WriteLine(
				$"\"{source.Name}\" -> \"{destNameForLogging}\". Only Source had a small number of unique files, and all {result.NumFilesInBothThatAreNotIdentical} divergent files were larger in the source.");

			return;
		}

		// Same check as above but for dest.
		if (result.NumUniqueDestFiles > 0
		    && result.NumUniqueDestFiles < 0.05 * result.NumFilesInDest
		    && result.NumUniqueSourceFiles == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInDest > 0.95 * result.NumFilesInBothSourceAndDest
		    && result.NumFilesInSource == result.NumFilesInBothSourceAndDest
		    && result.NumFilesInBothThatAreNotIdentical > 0
		    && result.NumFilesInBothThatAreNotIdentical < 0.05 * result.NumFilesInBothSourceAndDest
		    && result.NumLargerDivergentDestFiles == result.NumFilesInBothThatAreNotIdentical
		    && result.NumLargerDivergentSourceFiles == 0)
		{
			// All divergent dest files are larger, keep them.
			DeleteDirectory(source);
			Console.WriteLine(
				$"\"{source.Name}\" -> Deleted. Only Dest had a small number of unique files, and all {result.NumFilesInBothThatAreNotIdentical} divergent files were larger in the dest.");
			return;
		}

		// Very loose last-resort checks.
		// Even if there are conflicts and even if not all divergent files are larger in one direction, regardless of the number of files,
		// if there are unique files only in one version, prefer that version
		if (result.NumUniqueSourceFiles > 0
		    && result.NumUniqueDestFiles == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInSource > result.NumFilesInBothSourceAndDest
		    && result.NumFilesInDest == result.NumFilesInBothSourceAndDest)
		{
			// It is simpler to just copy all the source.
			var fullDestPath = dest.FullName;
			var destParent = dest.Parent;
			var destNameForLogging =
				destParent != null ? $"{destParent.Name}{Path.DirectorySeparatorChar}{dest.Name}" : dest.Name;
			DeleteDirectory(dest);
			Directory.Move(source.FullName, fullDestPath);
			Console.WriteLine(
				$"\"{source.Name}\" -> \"{destNameForLogging}\". Unique files were all in source. There were divergent files but source was preferred as a last resort.");
			return;
		}

		if (result.NumUniqueDestFiles > 0
		    && result.NumUniqueSourceFiles == 0
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumFilesInBothSourceAndDest > 0
		    && result.NumFilesInDest > result.NumFilesInBothSourceAndDest
		    && result.NumFilesInSource == result.NumFilesInBothSourceAndDest)
		{
			DeleteDirectory(source);
			Console.WriteLine(
				$"\"{source.Name}\" -> Deleted. Unique files were all in dest. There were divergent files but dest was preferred as a last resort.");
			return;
		}

		// One final check for if the source and dest look like 100% completely different songs. If so, and we
		// have a InputPackNameForConflicts specified, then copy the source over with a rename.
		if (!string.IsNullOrEmpty(InputPackNameForConflicts)
		    && !dest.Name.EndsWith($" ({InputPackNameForConflicts})")
		    && result.NumSubDirectoriesInBothSourceAndDest == 0
		    && result.NumUniqueSourceSubDirectories == 0
		    && result.NumUniqueDestSubDirectories == 0
		    && result.NumUniqueSourceFiles > 0.95 * result.NumFilesInSource
		    && result.NumUniqueDestFiles > 0.95 * result.NumFilesInDest
		    && result.NumFilesInBothSourceAndDest < 0.05 * result.NumFilesInSource
		    && result.NumFilesInBothSourceAndDest < 0.05 * result.NumFilesInDest
		    && result.NumFilesInBothThatAreNotIdentical < 0.01 * result.NumFilesInSource
		    && result.NumFilesInBothThatAreNotIdentical < 0.01 * result.NumFilesInDest)
		{
			var modifiedDestPath = $"{dest.FullName} ({InputPackNameForConflicts})";
			Directory.Move(source.FullName, modifiedDestPath);
			Console.WriteLine(
				$"\"{source.Name}\" -> \"{modifiedDestPath}\". Source and dest look like different songs. Kept dest and copied source over with \" ({InputPackNameForConflicts})\"");
			return;
		}

		// Failing all the above, we'll need to manually inspect the results and hand-merge.
		// We can only get here if both source and dest have unique files and there are conflicts.
		var sb = new StringBuilder();
		sb.Append($"UNSAFE CONFLICT: \"{source.Name}\":");
		sb.Append($"\n\tUnique Source Files: {result.NumUniqueSourceFiles}/{result.NumFilesInSource}.");
		sb.Append($"\n\tUnique Source Subdirectories: {result.NumUniqueSourceSubDirectories}.");
		sb.Append($"\n\tUnique Dest Files: {result.NumUniqueDestFiles}/{result.NumFilesInDest}.");
		sb.Append($"\n\tUnique Dest Subdirectories: {result.NumUniqueDestSubDirectories}.");
		sb.Append($"\n\tFiles in Both Source and Dest: {result.NumFilesInBothSourceAndDest}.");
		sb.Append($"\n\tFiles in Both Source and Dest Not Identical: {result.NumFilesInBothThatAreNotIdentical}.");
		sb.Append($"\n\tSubdirectories in Both Source and Dest: {result.NumSubDirectoriesInBothSourceAndDest}.");
		Console.WriteLine(sb);
	}

	private static DirectoryComparison HandleConflictRecursive(DirectoryInfo source, DirectoryInfo dest)
	{
		var result = new DirectoryComparison();

		var sourceFilePaths = Directory.GetFiles(source.FullName);
		var destFilePaths = Directory.GetFiles(dest.FullName);

		var sourceFiles = new FileInfo[sourceFilePaths.Length];
		for (var i = 0; i < sourceFilePaths.Length; i++)
			sourceFiles[i] = new FileInfo(sourceFilePaths[i]);

		var destFiles = new FileInfo[destFilePaths.Length];
		for (var i = 0; i < destFilePaths.Length; i++)
			destFiles[i] = new FileInfo(destFilePaths[i]);

		result.NumFilesInSource = sourceFiles.Length;
		result.NumFilesInDest = destFiles.Length;

		Array.Sort(sourceFiles, (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		Array.Sort(destFiles, (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

		// Compare files.
		var di = -1;
		for (var si = 0; si < sourceFiles.Length; si++)
		{
			var sourceFile = sourceFiles[si];
			while (true)
			{
				// We have hit the end of the dest files. The source file is unique.
				var nextDi = di + 1;
				if (nextDi >= destFiles.Length)
				{
					result.NumUniqueSourceFiles++;
					break;
				}

				// Compare the names of the current source file and next dest file.
				var nameComparison = string.Compare(sourceFile.Name, destFiles[nextDi].Name,
					StringComparison.CurrentCultureIgnoreCase);

				// Source file name still precedes next dest file name. This means the source file is not in the dest list.
				if (nameComparison < 0)
				{
					result.NumUniqueSourceFiles++;

					// Break out of the dest loop so we can process the next source file.
					break;
				}

				// The dest file name precedes the source file name. This means the next dest file is not present in the source file list.
				else if (nameComparison > 0)
				{
					result.NumUniqueDestFiles++;

					// Advance the loop so we can check the next dest file again.
					di = nextDi;
				}

				// A file with the same name is in both source and dest.
				else
				{
					di = nextDi;
					result.NumFilesInBothSourceAndDest++;
					var destFile = destFiles[di];

					// Check if the files are identical. Checking byte by byte / hashing is expensive. Just check lengths.
					if (sourceFile.Length != destFile.Length)
					{
						if (sourceFile.Length > destFile.Length)
							result.NumLargerDivergentSourceFiles++;
						else
							result.NumLargerDivergentDestFiles++;
						result.NumFilesInBothThatAreNotIdentical++;
					}

					break;
				}
			}
		}

		// Unprocessed dest files.
		di++;
		while (di < destFiles.Length)
		{
			result.NumUniqueDestFiles++;
			di++;
		}

		// Compare subdirectories.
		var sourceSubDirectoriesPaths = Directory.GetDirectories(source.FullName);
		var destSubDirectoriesPaths = Directory.GetDirectories(dest.FullName);

		var sourceSubDirectories = new DirectoryInfo[sourceSubDirectoriesPaths.Length];
		for (var i = 0; i < sourceSubDirectoriesPaths.Length; i++)
			sourceSubDirectories[i] = new DirectoryInfo(sourceSubDirectoriesPaths[i]);

		var destSubDirectories = new DirectoryInfo[destSubDirectoriesPaths.Length];
		for (var i = 0; i < destSubDirectoriesPaths.Length; i++)
			destSubDirectories[i] = new DirectoryInfo(destSubDirectoriesPaths[i]);

		Array.Sort(sourceSubDirectories, (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		Array.Sort(destSubDirectories, (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

		di = -1;
		for (var si = 0; si < sourceSubDirectories.Length; si++)
		{
			var sourceSubDirectory = sourceSubDirectories[si];
			while (true)
			{
				// We have hit the end of the dest subdirectories. The source subdirectory is unique.
				var nextDi = di + 1;
				if (nextDi >= destSubDirectories.Length)
				{
					result.NumUniqueSourceSubDirectories++;
					break;
				}

				// Compare the names of the current source subdirectory and next dest subdirectory.
				var nameComparison = string.Compare(sourceSubDirectory.Name, destSubDirectories[nextDi].Name,
					StringComparison.CurrentCultureIgnoreCase);

				// Source subdirectory name still precedes next dest subdirectory name. This means the source subdirectory is not in the dest list.
				if (nameComparison < 0)
				{
					result.NumUniqueSourceSubDirectories++;

					// Break out of the dest loop so we can process the next source file.
					break;
				}

				// The dest subdirectory name precedes the source subdirectory name. This means the next dest subdirectory is not present in the source subdirectory list.
				else if (nameComparison > 0)
				{
					result.NumUniqueDestSubDirectories++;

					// Advance the loop so we can check the next dest subdirectory again.
					di = nextDi;
				}

				// A subdirectory with the same name is in both source and dest.
				else
				{
					di = nextDi;
					result.NumSubDirectoriesInBothSourceAndDest++;
					var destSubDirectory = destSubDirectories[di];

					// Recurse.
					var subResult = HandleConflictRecursive(sourceSubDirectory, destSubDirectory);
					result.AddSubDirectoryComparision(subResult);
					break;
				}
			}
		}

		// Unprocessed dest subdirectories.
		di++;
		while (di < destSubDirectories.Length)
		{
			result.NumUniqueDestSubDirectories++;
			di++;
		}

		return result;
	}

	private static DirectoryInfo GetDirectoryForSongFolder(DirectoryInfo songFolder)
	{
		var folderName = songFolder.Name;
		var firstRune = Rune.GetRuneAt(folderName, 0);
		var code = firstRune.Value;

		switch (code)
		{
			// Kanji (CJK Unified Ideographs + extensions)
			case >= 0x4E00 and <= 0x9FFF:
			case >= 0x3400 and <= 0x4DBF:
			case >= 0x20000 and <= 0x2FA1F:
				return OutputKanji;
			// Hiragana
			case >= 0x3040 and <= 0x309F:
				return OutputHiragana;
			// Katakana (normal + halfwidth)
			case >= 0x30A0 and <= 0x30FF:
			case >= 0xFF66 and <= 0xFF9F:
				return OutputKatakana;
			// Numbers (ASCII + fullwidth)
			case >= 0x30 and <= 0x39:
			case >= 0xFF10 and <= 0xFF19:
				return OutputNumbers;
			// Letters (ASCII + fullwidth)
			case >= 0x41 and <= 0x5A:
			case >= 0x61 and <= 0x7A:
			case >= 0xFF21 and <= 0xFF3A:
			case >= 0xFF41 and <= 0xFF5A:
				return OutputAlphabetical[NormalizeUnicodeScalarLetter(code)];
			default:
				return OutputOthers;
		}
	}

	private static char NormalizeUnicodeScalarLetter(int code)
	{
		var asciiChar = code switch
		{
			>= 0xFF21 and <= 0xFF3A => (char)(code - 0xFF21 + 'A'),
			>= 0xFF41 and <= 0xFF5A => (char)(code - 0xFF41 + 'a'),
			_ => (char)code,
		};
		return char.ToUpperInvariant(asciiChar);
	}

	private static void DeleteDirectory(DirectoryInfo dir)
	{
		SetDirAttributesNormal(dir);
		Directory.Delete(dir.FullName, true);
	}

	private static void SetDirAttributesNormal(DirectoryInfo dir)
	{
		dir.Attributes = FileAttributes.Normal;
		foreach (var subDir in dir.GetDirectories())
			SetDirAttributesNormal(subDir);
		foreach (var file in dir.GetFiles())
			file.Attributes = FileAttributes.Normal;
	}
}
