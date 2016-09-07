﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Brimstone
{
	public abstract class QueueAction : ICloneable
	{
		public List<ActionGraph> Args { get; } = new List<ActionGraph>();
		public abstract ActionResult Run(Game game, IEntity source, ActionResult[] args);

		// TODO: Find a way to get rid of source altogether (we only need it for Play and Select at the moment)
		public ActionResult Execute(Game game, IEntity source, ActionResult[] args) {
			return Run(game, source, args);
		}

		public ActionGraph Then(ActionGraph g) {
			return ((ActionGraph) this).Then(g);
		}

		public override string ToString() {
			return GetType().Name;
		}

		public object Clone() {
			// A shallow copy is good enough: all properties and fields are value types
			// except for Args which is immutable
			return MemberwiseClone();
		}
	}
}