using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace Brimstone.Benchmarks
{
	class MultiBuildBenchmarks
	{
		public static bool CompilerOutput = false;

		static void Main(string[] args)
		{
			string usage = "Usage: mbbenchmarks --commit-range=oldest-commit-id[,newest-commit-id] [--compiler-output] [--base-path=path-to-solutions] [arguments-to-pass-to-benchmarks]\r\n\r\nIf no base path is specified, MBBenchmarks will search all ancestors of the current directory by default";
			string benchmarkArguments = string.Empty;
			string repoPath = string.Empty;
			string oldestCommitID = string.Empty;
			string newestCommitID = string.Empty;

			if (args.Length == 0) {
				Console.WriteLine(usage);
				return;
			}
			foreach (string a in args) {
				try {
					var arg = a;
					if (!arg.Contains("="))
						arg += "=";
					string name = arg.Substring(0, arg.IndexOf("=")).ToLower().Trim();
					string value = arg.Substring(name.Length + 1);
					switch (name) {
						case "--commit-range":
							if (value.Contains(",")) {
								oldestCommitID = value.Substring(0, value.IndexOf(",")).Trim();
								newestCommitID = value.Substring(value.IndexOf(",") + 1).Trim();
							} else {
								oldestCommitID = value;
							}
							break;
						case "--base-path":
							repoPath = value;
							break;
						case "--compiler-output":
							CompilerOutput = true;
							break;
						default:
							benchmarkArguments += name + "=" + value + " ";
							break;
					}
				}
				catch (Exception) {
					Console.WriteLine(usage);
					return;
				}
			}

			if (repoPath == string.Empty) {
				Console.Write("No base directory specified, searching... ");

				bool found = false;
				repoPath = "..";
				for (int depth = 0; depth < 20 && !found; depth++) {
					repoPath = Path.GetFullPath(repoPath);
					if (Directory.Exists(repoPath + @"\Brimstone") && Directory.Exists(repoPath + @"\BrimstoneBenchmarks"))
						found = true;
					else
						repoPath += @"\..";
				}
				if (!found) {
					Console.WriteLine("could not find solutions - please specify the base path with the --base-path option");
					return;
				}
				Console.WriteLine("found solutions at " + repoPath);
			}

			// Get commit log IDs (note: output does not include the oldest commit)
			string commitList = string.Empty;
			var procInfo = new ProcessStartInfo("git");
			procInfo.Arguments = "log --pretty=format:\"%H\" " + oldestCommitID + (newestCommitID.Length > 0? ".." + newestCommitID : "");
			procInfo.UseShellExecute = false;
			procInfo.WorkingDirectory = repoPath + @"\Brimstone";
			procInfo.RedirectStandardOutput = true;
			using (var p = Process.Start(procInfo)) {
				commitList = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
			}

			var commits = commitList.Split(new[] {'\n'}).Select(x => x.Trim()).ToList();
			commits.Add(oldestCommitID);
			commits.Reverse();

			if (newestCommitID.ToLower() == oldestCommitID.ToLower())
				commits = new List<string> { newestCommitID };

			// Produce benchmarks for each commit from oldest to newest
			var testNames = new List<string>();
			var resultSet = new List<List<string>>();
			bool gotNames = false;

			var csv = "Test Name,";

			foreach (var commitId in commits) {
				// Checkout selected commit
				Console.WriteLine("Checking out commit " + commitId);
				procInfo = new ProcessStartInfo("git");
				procInfo.Arguments = "checkout " + commitId;
				procInfo.UseShellExecute = false;
				procInfo.RedirectStandardInput = true;
				procInfo.WorkingDirectory = repoPath + @"\Brimstone";
				using (var p = Process.Start(procInfo))
					p.WaitForExit();

				// Build projects and run benchmarks
				List<string> results;
				Benchmarks(repoPath, benchmarkArguments, out results);

				// First 3 lines are header information
				if (results.Any()) {
					// Process results
					Console.WriteLine("Merging results...");

					csv += commitId.Substring(0, Math.Min(commitId.Length, 8)) + ",";
					var these = new List<string>();
					foreach (var r in results.Skip(3)) {
						var n = r.Split(new[] {','});
						these.Add(n[1]);
						if (!gotNames)
							testNames.Add(n[0]);
					}
					resultSet.Add(these);
					gotNames = true;
				}
			}

			// Produce CSV
			csv = csv.Substring(0, csv.Length - 1) + "\r\n";
			for (int row = 0; row < resultSet[0].Count; row++) {
				csv += testNames[row] + ",";
				for (int col = 0; col < resultSet.Count; col++)
					csv += resultSet[col][row] + ",";
				csv = csv.Substring(0, csv.Length - 1) + "\r\n";
			}
			File.WriteAllText(@"mbbenchmarks.csv", csv);
			Console.WriteLine("Results written to mbbenchmarks.csv");
		}

		public static void Benchmarks(string repoPath, string benchmarkArguments, out List<string> csv) {
			csv = new List<string>();

			// Restore NuGet Packages
			Console.WriteLine("Restoring NuGet packages...");
			var procInfo = new ProcessStartInfo("nuget");
			procInfo.Arguments = "restore " + repoPath + @"\Brimstone\Brimstone.sln";
			procInfo.UseShellExecute = false;
			procInfo.WorkingDirectory = Directory.GetCurrentDirectory();
			using (var ngProcess = Process.Start(procInfo))
				ngProcess.WaitForExit();

			// Build Brimstone
			if (!TryBuild("Brimstone", repoPath + @"\Brimstone\Brimstone\Brimstone.csproj"))
				return;

			// Build Benchmarks
			if (!TryBuild("Benchmarks", repoPath + @"\BrimstoneBenchmarks\Benchmarks\Benchmarks.csproj"))
				return;

			Console.WriteLine("Running benchmarks...\r\n");
			procInfo = new ProcessStartInfo(repoPath + @"\BrimstoneBenchmarks\Benchmarks\bin\Release\Benchmarks.exe");
			procInfo.Arguments = benchmarkArguments;
			procInfo.UseShellExecute = false;
			procInfo.WorkingDirectory = Directory.GetCurrentDirectory();
			using (var bmProcess = Process.Start(procInfo))
				bmProcess.WaitForExit();

			try {
				csv = File.ReadAllLines("benchmarks.csv").ToList();
				File.Delete("benchmarks.csv");
			}
			catch (FileNotFoundException) {
				Console.WriteLine("\r\nRunning benchmarks failed - no output produced - skipping");
			}
		}

		public static bool TryBuild(string name, string projectPath) {
			Project p = null;
			ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal);
			try
			{
				Console.Write("Building " + name + "...");
				p = new Project(projectPath);
				p.SetGlobalProperty("Configuration", "Release");

				bool success;
				if (CompilerOutput)
					success = p.Build(logger);
				else
					success = p.Build();

				if (success) {
					Console.WriteLine(" build successful");
				}
				else
				{
					Console.WriteLine(" build failed - skipping");
					return false;
				}
			}
			catch (InvalidProjectFileException)
			{
				Console.WriteLine(" could not find project at " + projectPath);
				return false;
			}
			finally
			{
				p.ProjectCollection.UnloadAllProjects();
			}
			return true;
		}
	}
}
