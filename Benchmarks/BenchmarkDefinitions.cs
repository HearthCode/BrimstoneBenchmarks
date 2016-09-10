﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brimstone.Actions;
using static Brimstone.Behaviours;

namespace Brimstone.Benchmark
{
	internal partial class Benchmarks {
		// ======================================================
		// =============== BENCHMARK DEFINITIONS ================
		// ======================================================

		public const int DefaultIterations = 100000;

		public void LoadDefinitions() {
			Tests = new Dictionary<string, Test>() {
				{ "RawClone", new Test("Raw cloning speed (full game; single-threaded)", Test_RawClone) },
				{ "RawCloneMT", new Test("Raw cloning speed (full game; multi-threaded)", Test_RawClone_MT) },
				{ "EffectiveClone", new Test("Effective cloning speed (full game; single-threaded)", Test_StoredClone) },
				{ "EffectiveCloneMT", new Test("Effective cloning speed (full game; multi-threaded)", Test_StoredClone_MT) },
				{ "BoomBotPreHit", new Test("Boom Bot pre-hit cloning test; RC + 2 BB per side", Test_BoomBotPreHit) },
				{ "BoomBotPreDeathrattle", new Test("Boom Bot pre-deathrattle cloning test; 5 RC + 2 BB per side", Test_BoomBotPreDeathrattle) },
				{ "BoomBotUniqueStatesNS", new Test("Boom Bot hit; fuzzy unique states; Naive; 5 BR + 2 BB per side", Test_BoomBotUniqueStatesNS, Default_Setup2, 1) },
				{ "BoomBotUniqueStatesDFS", new Test("Boom Bot hit; fuzzy unique states; DFS; 5 BR + 2 BB per side", Test_BoomBotUniqueStatesDFS, Default_Setup2, 1) },
				{ "BoomBotUniqueStatesBFS", new Test("Boom Bot hit; fuzzy unique states; BFS; 5 BR + 2 BB per side", Test_BoomBotUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles2UniqueStatesDFS", new Test("Arcane Missiles (2); fuzzy unique game states; DFS; 5 BR + 2 BB per side", Test_2AMUniqueStatesDFS, Default_Setup2, 1) },
				{ "ArcaneMissiles1UniqueStatesBFS", new Test("Arcane Missiles (1); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_1AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles2UniqueStatesBFS", new Test("Arcane Missiles (2); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_2AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles3UniqueStatesBFS", new Test("Arcane Missiles (3); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_3AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles4UniqueStatesBFS", new Test("Arcane Missiles (4); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_4AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles5UniqueStatesBFS", new Test("Arcane Missiles (5); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_5AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles6UniqueStatesBFS", new Test("Arcane Missiles (6); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_6AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles7UniqueStatesBFS", new Test("Arcane Missiles (7); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_7AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles8UniqueStatesBFS", new Test("Arcane Missiles (8); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_8AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles9UniqueStatesBFS", new Test("Arcane Missiles (9); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_9AMUniqueStatesBFS, Default_Setup2, 1) },
				{ "ArcaneMissiles10UniqueStatesBFS", new Test("Arcane Missiles (10); fuzzy unique game states; BFS; 5 BR + 2 BB per side", Test_10AMUniqueStatesBFS, Default_Setup2, 1) },
			};
		}

		// ======================================================
		// ===============    BENCHMARK CODE    =================
		// ======================================================

		public static Game Default_Setup() {
			return NewScenarioGame(MaxMinions: 7, NumBoomBots: 2, FillMinion: "River Crocolisk");
		}
		public static Game Default_Setup2() {
			return NewScenarioGame(MaxMinions: 7, NumBoomBots: 2, FillMinion: "Bloodfen Raptor", FillDeck: false);
		}

		public void Test_RawClone(Game g, int it) {
			Settings.ParallelClone = false;
			for (int i = 0; i < it; i++)
				g.CloneState();
		}
		public void Test_RawClone_MT(Game g, int it) {
			Settings.ParallelClone = false;
			Parallel.For(0, it, i => g.CloneState());
		}

		public void Test_StoredClone(Game g, int it) {
			Settings.ParallelClone = false;
			g.CloneStates(it);
		}

		public void Test_StoredClone_MT(Game g, int it) {
			Settings.ParallelClone = true;
			g.CloneStates(it);
		}

		public void Test_BoomBotPreHit(Game g, int it) {
			var BoomBotId = g.Player1.Board.First(t => t.Card.Id == "GVG_110t").Id;
			for (int i = 0; i < it; i++) {
				Game cloned = g.CloneState();
				((Minion)cloned.Entities[BoomBotId]).Hit(1);
			}
		}

		public void Test_BoomBotPreDeathrattle(Game g, int it) {
			// Capture after Boom Bot has died but before Deathrattle executes
			var BoomBot = g.Player1.Board.First(t => t.Card.Id == "GVG_110t");
			g.ActionQueue.OnAction += (o, e) => {
				ActionQueue queue = o as ActionQueue;
				if (e.Action is Death && e.Source == BoomBot) {
					for (int i = 0; i < it; i++) {
						Game cloned = g.CloneState();
						cloned.ActionQueue.ProcessAll();
					}
				}
			};
			BoomBot.Hit(1);
		}

		private void _boomBotUniqueStates(Game g, int it, ITreeActionWalker search) {
			var BoomBot = g.CurrentPlayer.Board.First(t => t.Card.Id == "GVG_110t");
			var tree = RandomOutcomeSearch.Build(
				Game: g,
				SearchMode: search,
				Action: () => {
					BoomBot.Hit(1);
				}
			);
		}
		public void Test_BoomBotUniqueStatesNS(Game g, int it) {
			_boomBotUniqueStates(g, it, new NaiveActionWalker());
		}

		public void Test_BoomBotUniqueStatesDFS(Game g, int it) {
			_boomBotUniqueStates(g, it, new DepthFirstActionWalker());
		}

		public void Test_BoomBotUniqueStatesBFS(Game g, int it) {
			_boomBotUniqueStates(g, it, new BreadthFirstActionWalker());
		}

		private void _missilesUniqueStates(Game g, int it, int missiles, ITreeActionWalker search) {
			Cards.FromName("Arcane Missiles").Behaviour.Battlecry = Damage(RandomOpponentHealthyCharacter, 1) * missiles;
			Cards.FromId("GVG_110t").Behaviour.Deathrattle = Damage(RandomOpponentHealthyMinion, RandomAmount(1, 4));

			var ArcaneMissiles = g.CurrentPlayer.Give("Arcane Missiles");
			var tree = RandomOutcomeSearch.Build(
				Game: g,
				SearchMode: search,
				Action: () => {
					ArcaneMissiles.Play();
				}
			);
		}

		public void Test_2AMUniqueStatesDFS(Game g, int it) {
			_missilesUniqueStates(g, it, 2, new DepthFirstActionWalker());
		}

		public void Test_1AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 1, new BreadthFirstActionWalker());
		}

		public void Test_2AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 2, new BreadthFirstActionWalker());
		}

		public void Test_3AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 3, new BreadthFirstActionWalker());
		}

		public void Test_4AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 4, new BreadthFirstActionWalker());
		}

		public void Test_5AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 5, new BreadthFirstActionWalker());
		}

		public void Test_6AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 6, new BreadthFirstActionWalker());
		}
		public void Test_7AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 7, new BreadthFirstActionWalker());
		}
		public void Test_8AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 8, new BreadthFirstActionWalker());
		}
		public void Test_9AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 9, new BreadthFirstActionWalker());
		}
		public void Test_10AMUniqueStatesBFS(Game g, int it) {
			_missilesUniqueStates(g, it, 10, new BreadthFirstActionWalker());
		}
	}
}
