using System;
using System.Windows.Forms;

namespace SanYingInput
{
	internal partial class ConfigForm : Form
	{
		internal struct ConfigFormSaveData
		{
			internal int switchS;
			internal int switchA1;
			internal int switchA2;
			internal int switchB1;
			internal int switchB2;
			internal int switchC1;
			internal int switchC2;
			internal int switchD;
			internal int switchE;
			internal int switchF;
			internal int switchG;
			internal int switchH;
			internal int switchI;
			internal int switchJ;
			internal int switchK;
			internal int switchL;
			internal int switchReverserFront;
			internal int switchReverserNeutral;
			internal int switchReverserBack;
			internal int switchHorn1;
			internal int switchHorn2;
			internal int switchMusicHorn;
			internal int switchConstSpeed;
		}

		internal ConfigFormSaveData Configuration;

		internal ConfigForm()
		{
			Configuration = new ConfigFormSaveData();
		}

		internal void LoadConfigurationFile(string path)
		{
			// Stub
		}

		internal void EnumerateDevices()
		{
			// Stub
		}
	}
}
