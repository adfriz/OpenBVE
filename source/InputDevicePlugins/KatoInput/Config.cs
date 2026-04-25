using System;

namespace KatoInput
{
	internal class Config : IDisposable
	{
		internal struct KatoInputConfiguration
		{
			internal bool ConvertNotches;
			internal bool KeepMinMax;
			internal bool MapHoldBrake;
		}

		internal KatoInputConfiguration Configuration;

		internal void LoadConfig()
		{
			// Stub
		}

		public void Dispose()
		{
			// Stub
		}
	}
}
