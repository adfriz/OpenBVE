using System;
using OpenBveApi.Sounds;

namespace SoundManager
{
	public abstract partial class SoundsBase
	{
		protected class SoundSourceAttenuation : IComparable<SoundSourceAttenuation>
		{
			public readonly SoundSource Source;
			public double Gain;
			public readonly double Distance;

			public SoundSourceAttenuation(SoundSource source, double gain, double distance)
			{
				Source = source;
				Gain = gain;
				Distance = distance;
			}
			int IComparable<SoundSourceAttenuation>.CompareTo(SoundSourceAttenuation other)
			{
				return other.Gain.CompareTo(Gain);
			}
		}

		public void Update(double timeElapsed, SoundModels model)
		{
			if (timeElapsed == 0.0 || timeElapsed > 0.5)
			{
				for (int i = 0; i < SourceCount; i++)
				{
					if (Sources[i].State == SoundSourceState.StopPending)
					{
						Sources[i].Stop();
						Sources[i] = Sources[SourceCount - 1];
						SourceCount--;
						i--;
					}
				}
				return;
			}

			UpdateInverseModel(timeElapsed);
		}
		
		protected abstract void UpdateInverseModel(double timeElapsed);
	}
}
