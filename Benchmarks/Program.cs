// NOTE: The benchmarking tools work with commit 1006ccd5 (4th Septmber 2016 - 6:04:14am) onwards

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Brimstone.Entities;

namespace Brimstone.Benchmark
{
	internal class Supervisor
	{
		private TextWriter cOut;
		private Stopwatch sw;
		private StringBuilder csv = new StringBuilder();
		private int timeoutMs = -1;

		public Supervisor(int timeout) {
			timeoutMs = timeout;
		}

		public void Run(Test test) {
			var testName = test.Name + (test.Iterations > 1 ? "; " + test.Iterations + " iterations" : "");
			var results = new List<long>();

			// Get all settings fields
			var settingsFields = typeof(Brimstone.Settings).GetFields(BindingFlags.Static | BindingFlags.Public);

			for (int i = 0; i < Benchmarks.DisabledOptionsSets.Count; i++) {
				// Enable all settings by default
				foreach (var field in settingsFields)
					if (field.FieldType.FullName == "Boolean")
						field.SetValue(null, true);

				// Disable the specified options
				foreach (var disable in Benchmarks.DisabledOptionsSets[i])
					settingsFields.First(s => s.Name == disable).SetValue(null, false);

				var game = test.SetupCode();
				if (timeoutMs != -1) {
					var cts = new CancellationTokenSource();
					Thread taskThread = null;
					cts.CancelAfter(timeoutMs);
					try {
						Task.Run((() => {
							taskThread = Thread.CurrentThread;
							Start(i == 0 ? testName : "");
							test.BenchmarkCode(game, test.Iterations);
							sw.Stop();
						})).Wait(cts.Token);
					}
					catch (OperationCanceledException) {
						taskThread.Abort();
					}
				} else {
					Start(i == 0 ? testName : "");
					test.BenchmarkCode(game, test.Iterations);
				}
				results.Add(Result().ElapsedMilliseconds);
			}
			Console.WriteLine();
			csv.Append(testName);
			foreach (var r in results)
				csv.Append("," + r);
			csv.AppendLine();
		}

		public void Start(string testName = null) {
			if (!string.IsNullOrEmpty(testName))
				Console.Write(testName.PadRight(120));
			cOut = Console.Out;
			Console.SetOut(TextWriter.Null);

			// Force wait until all garbage collection is completed
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			sw = new Stopwatch();
			sw.Start();
		}

		public Stopwatch Result() {
			sw.Stop();
			Console.SetOut(cOut);
			Console.Write((sw.ElapsedMilliseconds + "ms").PadRight(12));
			return sw;
		}

		public void WriteResults(string path) {
			// Make sub-test columns
			string testHeader = string.Empty;
			for (int i = 1; i <= Benchmarks.DisabledOptionsSets.Count; i++)
				testHeader += i + ",";
			testHeader = testHeader.Substring(0, testHeader.Length - 1);

			csv.Insert(0,
			"Build," +
#if DEBUG
			"Debug " +
#else
			"Release " +
#endif
			Assembly.GetAssembly(typeof(Game)).GetName().Version + "\r\n" +
			"\"\",\"\"\r\nTest Name," + testHeader + "\r\n");
			File.WriteAllText(path, csv.ToString());
		}
	}

	internal struct Test
	{
		public string Name;
		public int Iterations;
		public Func<Game> SetupCode;
		public Action<Game, int> BenchmarkCode;

		public Test(string ln, Action<Game, int> benchmark, Func<Game> setup = null, int it = Benchmarks.DefaultIterations) {
			Name = ln;
			Iterations = it;
			SetupCode = setup ?? Benchmarks.Default_Setup;
			BenchmarkCode = benchmark;
		}
	}

	internal partial class Benchmarks
	{
		public static List<List<string>> DisabledOptionsSets = new List<List<string>>();
		public Dictionary<string, Test> Tests;

		// Create and start a game with Player 1 as the first player and no decks
		public static Game NewEmptyGame() {
			var game = new Game(HeroClass.Druid, HeroClass.Druid, PowerHistory: false);
			Debug.Assert(game.Entities.Count == 6);
			game.Start(FirstPlayer: 1, SkipMulligan: true);
			return game;
		}

		// Create and start a game with Player 1 as the first player with randomly filled decks
		public static Game NewPopulatedGame() {
			var cOut = Console.Out;
			Console.SetOut(TextWriter.Null);
			var game = new Game(HeroClass.Druid, HeroClass.Druid, PowerHistory: false);
			game.Player1.Deck.Fill();
			game.Player2.Deck.Fill();
			Debug.Assert(game.Entities.Count == 66);
			game.Start(FirstPlayer: 1, SkipMulligan: true);
			Console.SetOut(cOut);
			return game;
		}

		// Create and start a game with Player 1 as the first player,
		// using MaxMinions per side of the board, of which NumBoomBots are Boom Bots
		// and the rest are the minion specified by the card name in FillMinion
		public static Game NewScenarioGame(int MaxMinions, int NumBoomBots, string FillMinion, bool FillDeck = true) {
			var cOut = Console.Out;
			Console.SetOut(TextWriter.Null);
			var game = new Game(HeroClass.Druid, HeroClass.Druid, PowerHistory: false);
			if (FillDeck) {
				game.Player1.Deck.Fill();
				game.Player2.Deck.Fill();
			}
			game.Start(FirstPlayer: 1, SkipMulligan: true);

			for (int i = 0; i < MaxMinions - NumBoomBots; i++)
				game.CurrentPlayer.Give(FillMinion).Play();
			for (int i = 0; i < NumBoomBots; i++)
				game.CurrentPlayer.Give("GVG_110t").Play();
			game.EndTurn();
			for (int i = 0; i < MaxMinions - NumBoomBots; i++)
				game.CurrentPlayer.Give(FillMinion).Play();
			for (int i = 0; i < NumBoomBots; i++)
				game.CurrentPlayer.Give("GVG_110t").Play();
			Console.SetOut(cOut);
			return game;
		}

		public void Run(string filter, int timeout) {
			var benchmark = new Supervisor(timeout);
			bool any = false;

			foreach (var kv in Tests)
				if (Regex.IsMatch(kv.Key.ToLower(), filter)) {
					Console.Write(("Test [" + kv.Key + "]: ").PadRight(60));
					benchmark.Run(kv.Value);
					any = true;
				}

			if (any) {
				var path = "benchmarks.csv";
				benchmark.WriteResults(path);
				Console.WriteLine("Benchmark results written to: " + path);
			} else {
				Console.WriteLine("No tests to run");
			}
		}

		static void Main(string[] args) {
			string filter = string.Empty;
			int timeout = -1;

			string usage = "Usage: benchmarks [--filter=regex] [--timeout=milliseconds] [--unset=disable1[,disable2...] [--unset=...]]...";

			// Get list of all valid settings names
			var settingsNames = typeof(Brimstone.Settings).GetFields(BindingFlags.Static | BindingFlags.Public).Select(s => s.Name);

			foreach (string a in args) {
				try {
					var arg = a;
					if (!arg.Contains("="))
						arg += "=";
					string name = arg.Substring(0, arg.IndexOf("=")).ToLower().Trim();
					string value = arg.Substring(name.Length + 1);
					switch (name) {
						case "--filter":
							filter += value.ToLower();
							break;
						case "--timeout":
							if (!int.TryParse(value, out timeout)) {
								Console.WriteLine(usage);
								return;
							}
							break;
						case "--unset":
							if (value.Length == 0)
								DisabledOptionsSets.Add(new List<string>());
							else {
								var settingsToDisable = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
								var casedSettingsToDisable = new List<string>();
								foreach (var s in settingsToDisable) {
									if (!settingsNames.Select(n => n.ToLower()).Contains(s.ToLower())) {
										Console.WriteLine("Invalid setting option: " + s + " - valid options are " + string.Join(", ", settingsNames));
										return;
									}
									// Use the exact casing
									casedSettingsToDisable.Add(settingsNames.First(n => n.ToLower() == s.ToLower()));
								}
								DisabledOptionsSets.Add(casedSettingsToDisable);
							}
							break;
						default:
							Console.WriteLine(usage);
							return;
					}
				} catch (Exception) {
					Console.WriteLine(usage);
					return;
				}
			}

			Console.WriteLine("Benchmarks for Brimstone build " + Assembly.GetAssembly(typeof(Game)).GetName().Version);
#if DEBUG
			Console.WriteLine("WARNING: Running in Debug mode. Benchmarks will perform worse than Release builds.");
#endif
			if (!string.IsNullOrEmpty(filter))
				Console.WriteLine("Running benchmarks using filter: " + filter);

			if (DisabledOptionsSets.Count == 0)
				DisabledOptionsSets.Add(new List<string>());

			for (int i = 0; i < DisabledOptionsSets.Count; i++) {
				Console.Write("Sub-test " + (i+1) + " ");
				var list = DisabledOptionsSets[i];
				if (list.Count == 0)
					Console.WriteLine("will enable all settings");
				else {
					Console.Write("will disable: ");
					foreach (var item in list)
						Console.Write(item + " ");
					Console.WriteLine("");
				}
			}
			Console.WriteLine("");

			var b = new Benchmarks();
			b.LoadDefinitions();
			b.Run(filter, timeout);
		}
	}
}
