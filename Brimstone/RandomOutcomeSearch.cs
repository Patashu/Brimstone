#define _TREE_DEBUG

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Brimstone
{
	public interface ITreeActionWalker
	{
		// (Optional) code to execute after each node's ActionQueue is processed or cancelled
		void Visitor(ProbabilisticGameNode cloned, GameTree<GameNode> tree, QueueActionEventArgs e);
		// (Optional) code to execute after each non-cancelled action in a node's ActionQueue completes
		void PostAction(ActionQueue q, GameTree<GameNode> tree, QueueActionEventArgs e);
		// (Optional) code to execute after all nodes have been processed
		// NOTE: This can start a new round of processing if desired (eg. BFS)
		Task PostProcess(GameTree<GameNode> tree);
		// Return all of the unique game states (leaf nodes) found in the search results
		Dictionary<Game, double> GetUniqueGames();
	}

	public class RandomOutcomeSearch : GameTree<GameNode>
	{
		public bool Parallel { get; }

		private ITreeActionWalker searcher = null;
		private Dictionary<Game, double> uniqueGames = new Dictionary<Game, double>();

		public RandomOutcomeSearch(Game Root, ITreeActionWalker SearchMode = null, bool? Parallel = null)
			: base(new ProbabilisticGameNode(Root, TrackChildren: false)) {
			this.Parallel = Parallel ?? Settings.ParallelTreeSearch;

			if (SearchMode == null)
				SearchMode = new BreadthFirstActionWalker();

			if (this.Parallel) {
				RootNode.Game.ActionQueue.ReplaceAction<RandomChoice>(replaceRandomChoiceParallel);
				RootNode.Game.ActionQueue.ReplaceAction<RandomAmount>(replaceRandomAmountParallel);
			}
			else {
				RootNode.Game.ActionQueue.ReplaceAction<RandomChoice>(replaceRandomChoice);
				RootNode.Game.ActionQueue.ReplaceAction<RandomAmount>(replaceRandomAmount);
			}

			RootNode.Game.ActionQueue.OnAction += (o, e) => {
				searcher.PostAction(o as ActionQueue, this, e);
			};
			searcher = SearchMode;
		}

		public Dictionary<Game, double> GetUniqueGames() {
			if (uniqueGames.Count > 0)
				return uniqueGames;
			uniqueGames = searcher.GetUniqueGames();
			return uniqueGames;
		}

		public void Run(Action Action) {
			RootNode.Game.ActionQueue.UserData = RootNode;
			RootNode.Game.Entities.Changed = false;
			Action();
			searcher.PostProcess(this).Wait();
		}

		public async Task RunAsync(Action Action) {
			RootNode.Game.ActionQueue.UserData = RootNode;
			RootNode.Game.Entities.Changed = false;
			await Task.Run(() => Action());
			await searcher.PostProcess(this);
		}

		public static RandomOutcomeSearch Build(Game Game, Action Action, ITreeActionWalker SearchMode = null) {
			var tree = new RandomOutcomeSearch(Game, SearchMode);
			tree.Run(Action);
			return tree;
		}

		public static async Task<RandomOutcomeSearch> BuildAsync(Game Game, Action Action, ITreeActionWalker SearchMode = null) {
			var tree = new RandomOutcomeSearch(Game, SearchMode);
			await tree.RunAsync(Action);
			return tree;
		}

		public static Dictionary<Game, double> Find(Game Game, Action Action, ITreeActionWalker SearchMode = null) {
			return Build(Game, Action, SearchMode).GetUniqueGames();
		}

		public static async Task<Dictionary<Game, double>> FindAsync(Game Game, Action Action, ITreeActionWalker SearchMode = null) {
			var tree = await BuildAsync(Game, Action, SearchMode);
			return tree.GetUniqueGames();
		}

		protected Task replaceRandomChoice(ActionQueue q, QueueActionEventArgs e) {
			// Choosing a random entity (minion in this case)
			// Clone and start processing for every possibility
#if _TREE_DEBUG
			DebugLog.WriteLine("");
			DebugLog.WriteLine("--> Depth: " + e.Game.Depth);
#endif
			double perItemWeight = 1.0 / e.Args[RandomChoice.ENTITIES].Count();
			foreach (Entity entity in e.Args[RandomChoice.ENTITIES]) {
				// When cloning occurs, RandomChoice has been pulled from the action queue,
				// so we can just insert a fixed item at the start of the queue and restart the queue
				// to effectively replace it
				var clonedNode = ((ProbabilisticGameNode)e.UserData).Branch(perItemWeight);
				NodeCount++;
				clonedNode.Game.ActionQueue.InsertDeferred(e.Source, entity);
				clonedNode.Game.ActionQueue.ProcessAll(clonedNode);
				searcher.Visitor(clonedNode, this, e);
			}
#if _TREE_DEBUG
			DebugLog.WriteLine("<-- Depth: " + e.Game.Depth);
			DebugLog.WriteLine("");
#endif
			return Task.FromResult(0);
		}

		protected Task replaceRandomAmount(ActionQueue q, QueueActionEventArgs e) {
			// Choosing a random value (damage amount in this case)
			// Clone and start processing for every possibility
#if _TREE_DEBUG
			DebugLog.WriteLine("");
			DebugLog.WriteLine("--> Depth: " + e.Game.Depth);
#endif
			double perItemWeight = 1.0 / ((e.Args[RandomAmount.MAX] - e.Args[RandomAmount.MIN]) + 1);
			for (int i = e.Args[RandomAmount.MIN]; i <= e.Args[RandomAmount.MAX]; i++) {
				// When cloning occurs, RandomAmount has been pulled from the action queue,
				// so we can just insert a fixed number at the start of the queue and restart the queue
				// to effectively replace it
				var clonedNode = ((ProbabilisticGameNode)e.UserData).Branch(perItemWeight);
				NodeCount++;
				clonedNode.Game.ActionQueue.InsertDeferred(e.Source, i);
				clonedNode.Game.ActionQueue.ProcessAll(clonedNode);
				searcher.Visitor(clonedNode, this, e);
			}
#if _TREE_DEBUG
			DebugLog.WriteLine("<-- Depth: " + e.Game.Depth);
			DebugLog.WriteLine("");
#endif
			return Task.FromResult(0);
		}

		protected async Task replaceRandomChoiceParallel(ActionQueue q, QueueActionEventArgs e) {
			// Choosing a random entity (minion in this case)
			// Clone and start processing for every possibility
#if _TREE_DEBUG
			DebugLog.WriteLine("");
			DebugLog.WriteLine("--> Depth: " + e.Game.Depth);
#endif
			double perItemWeight = 1.0 / e.Args[RandomChoice.ENTITIES].Count();
			await Task.WhenAll(
				e.Args[RandomChoice.ENTITIES].Select(entity =>
					Task.Run(async () => {
						// When cloning occurs, RandomChoice has been pulled from the action queue,
						// so we can just insert a fixed item at the start of the queue and restart the queue
						// to effectively replace it
						var clonedNode = ((ProbabilisticGameNode)e.UserData).Branch(perItemWeight);
						NodeCount++;
						clonedNode.Game.ActionQueue.InsertDeferred(e.Source, (Entity)entity);
						await clonedNode.Game.ActionQueue.ProcessAllAsync(clonedNode);
						searcher.Visitor(clonedNode, this, e);
					})
				)
			);
#if _TREE_DEBUG
			DebugLog.WriteLine("<-- Depth: " + e.Game.Depth);
			DebugLog.WriteLine("");
#endif
		}

		protected async Task replaceRandomAmountParallel(ActionQueue q, QueueActionEventArgs e) {
			// Choosing a random value (damage amount in this case)
			// Clone and start processing for every possibility
#if _TREE_DEBUG
			DebugLog.WriteLine("");
			DebugLog.WriteLine("--> Depth: " + e.Game.Depth);
#endif
			double perItemWeight = 1.0 / ((e.Args[RandomAmount.MAX] - e.Args[RandomAmount.MIN]) + 1);
			await Task.WhenAll(
				Enumerable.Range(e.Args[RandomAmount.MIN], (e.Args[RandomAmount.MAX] - e.Args[RandomAmount.MIN]) + 1).Select(i =>
					Task.Run(async () => {
						// When cloning occurs, RandomAmount has been pulled from the action queue,
						// so we can just insert a fixed number at the start of the queue and restart the queue
						// to effectively replace it
						var clonedNode = ((ProbabilisticGameNode)e.UserData).Branch(perItemWeight);
						NodeCount++;
						clonedNode.Game.ActionQueue.InsertDeferred(e.Source, i);
						await clonedNode.Game.ActionQueue.ProcessAllAsync(clonedNode);
						searcher.Visitor(clonedNode, this, e);
					})
				)
			);
#if _TREE_DEBUG
			DebugLog.WriteLine("<-- Depth: " + e.Game.Depth);
			DebugLog.WriteLine("");
#endif
		}
	}

	public abstract class TreeActionWalker : ITreeActionWalker
	{
		public abstract Dictionary<Game, double> GetUniqueGames();
		public virtual void PostAction(ActionQueue q, GameTree<GameNode> tree, QueueActionEventArgs e) { }
		public virtual Task PostProcess(GameTree<GameNode> tree) { return Task.FromResult(0); }
		public virtual void Visitor(ProbabilisticGameNode cloned, GameTree<GameNode> tree, QueueActionEventArgs e) { }
	}

	public class NaiveActionWalker : TreeActionWalker
	{
		private HashSet<ProbabilisticGameNode> leafNodeGames = new HashSet<ProbabilisticGameNode>();

		public override void Visitor(ProbabilisticGameNode cloned, GameTree<GameNode> tree, QueueActionEventArgs e) {
			// If the action queue is empty, we have reached a leaf node game state
			// TODO: Optimize to use TLS and avoid spinlocks
			if (cloned.Game.ActionQueue.Queue.Count == 0) {
				tree.LeafNodeCount++;
				lock (leafNodeGames) {
					leafNodeGames.Add(cloned);
				}
			}
		}

		public override Dictionary<Game, double> GetUniqueGames() {
			var uniqueGames = new Dictionary<Game, double>();

			// TODO: Parallelize
			while (leafNodeGames.Count > 0) {
				var root = leafNodeGames.Take(1).ToList()[0];
				leafNodeGames.Remove(root);
				uniqueGames.Add(root.Game, root.Probability);
				var different = new HashSet<ProbabilisticGameNode>();

				// Hash every entity
				// WARNING: This relies on a good hash function!
				var e1 = new HashSet<IEntity>(root.Game.Entities, new FuzzyEntityComparer());

				foreach (var n in leafNodeGames) {
					if (n.Game.Entities.Count != root.Game.Entities.Count) {
						different.Add(n);
						continue;
					}
					var e2 = new HashSet<IEntity>(n.Game.Entities, new FuzzyEntityComparer());
#if _TREE_DEBUG
					if (e2.Count < n.Game.Entities.Count || e1.Count < root.Game.Entities.Count) {
						// Potential hash collision
						var c = (e2.Count < n.Game.Entities.Count ? e2 : e1);
						var g2 = (c == e2 ? n.Game : root.Game);
						var ent = g2.Entities.Select(x => x.FuzzyHash).ToList();
						// Build list of collisions
						var collisions = new Dictionary<int, IEntity>();
						foreach (var e in g2.Entities) {
							if (collisions.ContainsKey(e.FuzzyHash)) {
								// It's not a coliision if the tag set differs only by entity ID
								bool collide = false;
								foreach (var tagPair in e.Zip(collisions[e.FuzzyHash], (x, y) => new { A = x, B = y })) {
									if (tagPair.A.Key == GameTag.ENTITY_ID && tagPair.B.Key == GameTag.ENTITY_ID)
										continue;
									if (tagPair.A.Key != tagPair.A.Key || tagPair.B.Value != tagPair.B.Value) {
										collide = true;
										break;
									}
								}
								if (collide) {
									DebugLog.WriteLine(collisions[e.FuzzyHash].ToString());
									DebugLog.WriteLine(e.ToString());
									DebugLog.WriteLine(collisions[e.FuzzyHash].FuzzyHash + " " + e.FuzzyHash);
									throw new Exception("Hash collision - not safe to compare games");
								}
							}
							else
								collisions.Add(e.FuzzyHash, e);
						}
					}
#endif
					if (!e2.SetEquals(e1)) {
						different.Add(n);
					}
					else {
						uniqueGames[root.Game] += root.Probability;
					}
				}
				leafNodeGames = different;
#if _TREE_DEBUG
				DebugLog.WriteLine("{0} games remaining to process ({1} unique games found so far)", different.Count, uniqueGames.Count);
#endif
			}
			return uniqueGames;
		}
	}

	public class DepthFirstActionWalker : TreeActionWalker
	{
		private Dictionary<Game, double> uniqueGames = new Dictionary<Game, double>(new FuzzyGameComparer());

		public override void Visitor(ProbabilisticGameNode cloned, GameTree<GameNode> tree, QueueActionEventArgs e) {
			// If the action queue is empty, we have reached a leaf node game state
			// so compare it for equality with other final game states
			if (cloned.Game.ActionQueue.Queue.Count == 0)
				if (!cloned.Game.EquivalentTo(e.Game)) {
					tree.LeafNodeCount++;
					// This will cause the game to be discarded if its fuzzy hash matches any other final game state
					// TODO: Optimize to use TLS and avoid spinlocks
					lock (uniqueGames) {
						if (!uniqueGames.ContainsKey(cloned.Game)) {
							uniqueGames.Add(cloned.Game, cloned.Probability);
#if _TREE_DEBUG
							DebugLog.WriteLine("UNIQUE GAME FOUND ({0})", uniqueGames.Count);
#endif
						}
						else {
							uniqueGames[cloned.Game] += cloned.Probability;
#if _TREE_DEBUG
							DebugLog.WriteLine("DUPLICATE GAME FOUND");
#endif
						}
					}
				}
		}

		public override Dictionary<Game, double> GetUniqueGames() {
			return uniqueGames;
		}
	}

	public class BreadthFirstActionWalker : TreeActionWalker
	{
		// The maximum number of task threads to split the search queue up into
		public int MaxDegreesOfParallelism { get; set; } = 7;

		// The minimum number of game nodes that have to be in the queue in order to activate parallelization
		// for a particular depth
		public int MinNodesToParallelize { get; set; } = 100;

		// All of the unique leaf node games found
		private ConcurrentDictionary<Game, ProbabilisticGameNode> uniqueGames = new ConcurrentDictionary<Game, ProbabilisticGameNode>(new FuzzyGameComparer());

		// The pruned search queue for the current search depth
		//private ConcurrentDictionary<Game, ProbabilisticGameNode> searchQueue = new ConcurrentDictionary<Game, ProbabilisticGameNode>(new FuzzyGameComparer());
		// Makes a ConcurrentQueue with GetConsumingEnumerable and Take/TryTake
		private BlockingCollection<ProbabilisticGameNode> searchQueue = new BlockingCollection<ProbabilisticGameNode>(new ConcurrentBag<ProbabilisticGameNode>());
		private BlockingCollection<ProbabilisticGameNode> pruneQueue = new BlockingCollection<ProbabilisticGameNode>(new ConcurrentBag<ProbabilisticGameNode>());
		private HashSet<int> visitedNodes = new HashSet<int>();

		//private long NodesRemainingTotal;

		// When an in-game action completes, check if the game state has changed
		// Some actions (like selectors) won't cause the game state to change,
		// so we continue running these until a game state change occurs

		// PRODUCER
		public override void PostAction(ActionQueue q, GameTree<GameNode> t, QueueActionEventArgs e) {
			// This game will be on the same thread as the calling task in parallel mode if it hasn't been cloned
			// If it has been cloned, it may be on a different thread
			if (e.Game.Entities.Changed) {
#if _TREE_DEBUG
				var id = e.Game.GameId;
				var hash = e.Game.Entities.FuzzyGameHash;
				DebugLog.WriteLine("PostAction for {0}:{1:x8}", id, hash);
				DebugLog.WriteLine("Game state CHANGED for {0}:{1:x8} after: {2}", id, hash, e);
#endif
				// The game state has changed so add it to the search queue
				//bool added = true;
				pruneQueue.Add(e.UserData as ProbabilisticGameNode);
				/*
				searchQueue.AddOrUpdate(e.Game, e.UserData as ProbabilisticGameNode,
					(game, node) => {
						node.Probability += ((ProbabilisticGameNode)e.UserData).Probability;
#if _TREE_DEBUG
						DebugLog.WriteLine("Pruned duplicate search node {0}:{1:x8}", id, hash);
#endif
						added = false;
						return node;
					});
					*/
				//if (added) {
					//Interlocked.Increment(ref NodesRemainingTotal);
#if _TREE_DEBUG
					DebugLog.WriteLine("Queued new node {0}:{1:x8}", id, hash);
#endif
				//}
				e.Cancel = true;
			}
#if _TREE_DEBUG
			else {
				var id = e.Game.GameId;
				var hash = e.Game.Entities.FuzzyGameHash;
				DebugLog.WriteLine("PostAction for {0}:{1:x8}", id, hash);
				DebugLog.WriteLine("Game state unchanged for {0}:{1:x8} after: {2}", id, hash, e);
			}
#endif
		}

		private async Task consumer() {
#if _TREE_DEBUG
			DebugLog.WriteLine("Starting search queue consumer");
#endif
			//while (Interlocked.Read(ref NodesRemaining) > 0) {
			// Blocks until an item becomes available
			foreach (var node in searchQueue.GetConsumingEnumerable()) {
#if _TREE_DEBUG
				//DebugLog.WriteLine("Games in search queue: {0}", searchQueue.Count + 1);
						//string.Concat(searchQueue.Select(x => string.Format(" {0}:{1:x8}", x.Game.GameId, x.Game.Entities.FuzzyGameHash))));
#endif
				// Sleep until something is queued

				// Get whatever item
				/*var game = searchQueue.First().Key;
				ProbabilisticGameNode node;
				searchQueue.TryRemove(game, out node);*/
#if _TREE_DEBUG
					var originalHash = node.Game.Entities.FuzzyGameHash;

					DebugLog.WriteLine("Dequeuing node {0}:{1:x8} - NodesRemaining before dequeue = " + Interlocked.Read(ref NodesRemainingThisDepth), node.Game.GameId, originalHash);
#endif
				// If the action queue is empty, we have reached a leaf node game state
				// so compare it for equality with other final game states
				if (node.Game.ActionQueue.Queue.Count == 0) {
					_tree.LeafNodeCount++;
					// This will cause the game to be discarded if its fuzzy hash matches any other final game state
					// TODO: Maybe we can defer the write?
#if _TREE_DEBUG
						//bool isNew = true;
#endif
					/*	uniqueGames.AddOrUpdate(node.Game, node,
							(g, n) => {
								n.Probability += node.Probability;
	#if _TREE_DEBUG
									DebugLog.WriteLine("DUPLICATE GAME FOUND");
									isNew = false;
	#endif
									return n;
							});
	#if _TREE_DEBUG
							if (isNew) {
								DebugLog.WriteLine("UNIQUE GAME FOUND ({0})", uniqueGames.Count);
							}
	#endif
					*/
					var r = Interlocked.Decrement(ref NodesRemainingThisDepth);
					//var o = Interlocked.Read(ref NodesRemainingTotal);
					//if (o == 0)
					//searchQueue.CompleteAdding();
					//Console.WriteLine("NodesRemaining: {0} / Queue size: {1} / Unique Games: {2} / RemainingThisDepth: {3}", o, searchQueue.Count, uniqueGames.Count, r);
					if (r == 0)
						lock (_pruneLock) Monitor.Pulse(_pruneLock);
				}
				else {
					await node.Game.ActionQueue.ProcessAllAsync(node);
					var r = Interlocked.Decrement(ref NodesRemainingThisDepth);
					//var o = Interlocked.Read(ref NodesRemainingTotal);
					//if (o == 0)
					//searchQueue.CompleteAdding();
					//Console.WriteLine("NodesRemaining: {0} / Queue size: {1} / Unique Games: {2} / RemainingThisDepth: {3}", o, searchQueue.Count, uniqueGames.Count, r);
					if (r == 0)
						lock (_pruneLock) Monitor.Pulse(_pruneLock);
				}

				// This node has now been fully processed
				//					Interlocked.Decrement(ref NodesRemaining);
#if _TREE_DEBUG
					DebugLog.WriteLine("Processed node {0}:{1:x8} - NodesRemaining after dequeue = " + Interlocked.Read(ref NodesRemainingThisDepth), node.Game.GameId, originalHash);
#endif
			}
		}

		// This is the entry point after the root node has been pushed into the queue
		// and the first change to the game has occurred

		private GameTree<GameNode> _tree;
		private long NodesRemainingThisDepth = 0;
		private object _pruneLock = new object();

		private void prune() {
			bool workRemaining;
#if _TREE_DEBUG
			int depth = 0;
#endif
			do {
#if _TREE_DEBUG
				DebugLog.WriteLine("DEPTH {0} - Starting new prune - checking {1} nodes", ++depth, pruneQueue.Count);
				int unpruned = 0;
#endif
				workRemaining = false;
				var localPruneQueue = pruneQueue.ToList();
				pruneQueue = new BlockingCollection<ProbabilisticGameNode>(new ConcurrentBag<ProbabilisticGameNode>());
				foreach (var node in localPruneQueue) {
					if (!visitedNodes.Contains(node.Game.Entities.FuzzyGameHash)) {
						Interlocked.Increment(ref NodesRemainingThisDepth);
						visitedNodes.Add(node.Game.Entities.FuzzyGameHash);
						searchQueue.Add(node);
						workRemaining = true;
#if _TREE_DEBUG
						unpruned++;
#endif
					}
					//var o = Interlocked.Decrement(ref NodesRemainingTotal);
					//Console.WriteLine("NodesRemaining {0} - pruned node", o);
				}
#if _TREE_DEBUG
				DebugLog.WriteLine("DEPTH {0} - Finished queuing up {1} unpruned nodes", depth, unpruned);
#endif
				if (workRemaining)
					lock (_pruneLock) {
						if (Interlocked.Read(ref NodesRemainingThisDepth) > 0)
							Monitor.Wait(_pruneLock);
					}
				else
					searchQueue.CompleteAdding();
			} while (workRemaining);
#if _TREE_DEBUG
			DebugLog.WriteLine("Prune queue task exiting");
#endif
		}

		public override async Task PostProcess(GameTree<GameNode> t) {
			_tree = t;

			var _pruneTask = Task.Run((Action)prune);

			// Make a single consumer
			var consumers = new List<Task>();
			for (int i = 0; i < MaxDegreesOfParallelism; i++)
				consumers.Add(Task.Run(consumer));

			await _pruneTask;
			await Task.WhenAll(consumers);
			//Console.WriteLine(NodesRemainingTotal);
			// TODO: Cancel prune task

			/*
			// Breadth-first processing loop
			do {
#if _TREE_DEBUG
				DebugLog.WriteLine("QUEUE SIZE: " + searchQueue.Count);
#endif
				// Quit if we have processed all nodes and none of them have children (all leaf nodes)
				if (searchQueue.Count == 0)
					break;

				// Copy the search queue and clear the current one; it will be refilled
				var nextQueue = new Dictionary<Game, ProbabilisticGameNode>(searchQueue);
				searchQueue.Clear();

				// Only parallelize if there are sufficient nodes to do so
				if (nextQueue.Count >= MinNodesToParallelize && ((RandomOutcomeSearch)t).Parallel) {
					// Process each game's action queue until it is interrupted by OnAction above
					await Task.WhenAll(
						// Split search queue into MaxDegreesOfParallelism partitions
						from partition in Partitioner.Create(nextQueue).GetPartitions(MaxDegreesOfParallelism)
							// Run each partition in its own task
						select Task.Run(async delegate {
#if _TREE_DEBUG
							var count = 0;
							DebugLog.WriteLine("Start partition run");
#endif
							using (partition)
								while (partition.MoveNext()) {
									// Process each node
									var kv = partition.Current;
									await kv.Key.ActionQueue.ProcessAllAsync(kv.Value);
#if _TREE_DEBUG
									count++;
#endif
								}
#if _TREE_DEBUG
							DebugLog.WriteLine("End run with partition size {0}", count);
#endif
						}));
#if _TREE_DEBUG
				DebugLog.WriteLine("=======================");
				DebugLog.WriteLine("CLONES SO FAR: " + t.NodeCount + " / " + t.LeafNodeCount);
				DebugLog.WriteLine("UNIQUE GAMES SO FAR: " + uniqueGames.Count);
				DebugLog.WriteLine("NEW QUEUE SIZE: " + searchQueue.Count + "\r\n");
#endif
				}
				else {
#if _TREE_DEBUG
					DebugLog.WriteLine("Start single-threaded run");
#endif
					// Process each node in the search queue sequentially
					foreach (var kv in nextQueue) {
						await kv.Key.ActionQueue.ProcessAllAsync(kv.Value);
					}
#if _TREE_DEBUG
					DebugLog.WriteLine("End single-threaded run");
#endif
				}
			} while (true);
			*/
		}

		public override Dictionary<Game, double> GetUniqueGames() {
			return uniqueGames.ToDictionary(x => x.Key, x => x.Value.Probability);
		}
	}
}
