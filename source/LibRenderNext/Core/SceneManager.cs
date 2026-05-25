using System.Collections.Generic;
using OpenBveApi.Math;
using OpenBveApi.Objects;

namespace LibRenderNext.Core
{
	public class SceneManager
	{
		public readonly BaseRenderer Renderer;

		public List<ObjectState> StaticObjectStates = new List<ObjectState>();
		public List<ObjectState> DynamicObjectStates = new List<ObjectState>();
		
		public object VisibilityUpdateLock = new object();

		public SceneManager(BaseRenderer renderer)
		{
			Renderer = renderer;
		}

		public void Clear()
		{
			lock (VisibilityUpdateLock)
			{
				StaticObjectStates.Clear();
				DynamicObjectStates.Clear();
			}
		}

		public void Reset()
		{
			Clear();
		}
	}
}
