﻿using System;
using System.Collections.Generic;

namespace Brimstone
{
	public class Behaviour
	{
		// Defaulting to null for unimplemented cards or actions
		public ActionGraph Battlecry;
		public ActionGraph Deathrattle;
	}

	public partial class CardBehaviour
	{
		// Factory functions for DSL syntax
		public static QueueAction BeginTurn { get { return new BeginTurn(); } }

		public static ActionGraph Draw(ActionGraph player) { return new Draw { Args = { player } }; }
		public static ActionGraph Give(ActionGraph player, ActionGraph card) { return new Give { Args = { player, card } }; }
		public static ActionGraph Play(ActionGraph player, ActionGraph entity) { return new Play { Args = { player, entity } }; }

		public static ActionGraph CreateMulligan(ActionGraph player) { return Select(player, p => ((Player)p).Hand.Slice(1, ((Player)p).NumCardsDrawnThisTurn)); }

		public static QueueAction RandomOpponentMinion { get { return new RandomOpponentMinion(); } }
		public static QueueAction AllMinions { get { return new AllMinions(); } }
		public static ActionGraph RandomAmount(ActionGraph min, ActionGraph max) { return new RandomAmount { Args = { min, max } }; }

		public static QueueAction Select(ActionGraph source, Func<IEntity, List<IEntity>> selector) { return new Selector { Args = { source }, Lambda = selector }; }

		public static ActionGraph Damage(ActionGraph target, ActionGraph amount) { return new Damage { Args = { target, amount } }; }
	}
}