using System.Linq;
using OpenBveApi.Hosts;
using OpenBveApi.Sounds;
using SoundManager;
using TrainManager.Trains;

namespace OpenBve
{
	internal partial class Sounds : SoundsBase
	{
		public override void StopAllSounds(object train)
		{
			if (!(train is TrainBase t))
			{
				return;
			}
			for (int i = 0; i < SourceCount; i++)
			{
				if (Sources[i] != null && (t.Cars.Contains(Sources[i].Parent) || Sources[i].Parent == train))
				{
					Sources[i].Stop();
				}
			}
		}

		public Sounds(HostInterface currentHost) : base(currentHost)
		{
		}
	}
}
