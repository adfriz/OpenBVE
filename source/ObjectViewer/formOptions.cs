using System;
using System.Windows.Forms;
using LibRender2.Viewports;
using ObjectViewer.Graphics;
using OpenBveApi;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Input;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenTK.Graphics;

namespace ObjectViewer
{
	public partial class formOptions : Form
	{
		private formOptions()
		{
			InitializeComponent();
			InterpolationMode.SelectedIndex = (int) Interface.CurrentOptions.Interpolation;
			AnisotropicLevel.Value = Interface.CurrentOptions.AnisotropicFilteringLevel;
			AntialiasingLevel.Value = Interface.CurrentOptions.AntiAliasingLevel;
			nearClip.Value = (decimal)Interface.CurrentOptions.NearClipBase;
			if (Translations.CurrentLanguageCode != "en-US")
			{
				labelNearClip.Text = Translations.GetInterfaceString(OpenBveApi.Hosts.HostApplication.OpenBve, new[] { "options", "quality_distance_nearclip" });
			}
			TransparencyQuality.SelectedIndex = Interface.CurrentOptions.TransparencyMode == TransparencyMode.Performance ? 0 : 2;
			width.Value = Program.Renderer.Screen.Width;
			height.Value = Program.Renderer.Screen.Height;
			comboBoxNewXParser.SelectedIndex = (int) Interface.CurrentOptions.CurrentXParser;
			comboBoxNewObjParser.SelectedIndex = (int) Interface.CurrentOptions.CurrentObjParser;
			comboBoxOptimizeObjects.SelectedIndex = (int)Interface.CurrentOptions.ObjectOptimizationMode;
			
			// Loading current shadow settings
			switch (Interface.CurrentOptions.ShadowResolution)
			{
				case ShadowMapResolution.Off: comboBoxShadowResolution.SelectedIndex = 0; break;
				case ShadowMapResolution.Low: comboBoxShadowResolution.SelectedIndex = 1; break;
				case ShadowMapResolution.Medium: comboBoxShadowResolution.SelectedIndex = 2; break;
				case ShadowMapResolution.High: comboBoxShadowResolution.SelectedIndex = 3; break;
				case ShadowMapResolution.Ultra: comboBoxShadowResolution.SelectedIndex = 4; break;
				default: comboBoxShadowResolution.SelectedIndex = 3; break;
			}

			switch (Interface.CurrentOptions.ShadowDrawDistance)
			{
				case ShadowDistance.Near: comboBoxShadowDistance.SelectedIndex = 0; break;
				case ShadowDistance.Medium: comboBoxShadowDistance.SelectedIndex = 1; break;
				case ShadowDistance.Far: comboBoxShadowDistance.SelectedIndex = 2; break;
				case ShadowDistance.VeryFar: comboBoxShadowDistance.SelectedIndex = 3; break;
				case ShadowDistance.ViewingDistance: comboBoxShadowDistance.SelectedIndex = 4; break;
				default: comboBoxShadowDistance.SelectedIndex = 1; break;
			}

			switch (Interface.CurrentOptions.ShadowCascades)
			{
				case ShadowCascadeCount.Two: comboBoxShadowCascades.SelectedIndex = 0; break;
				case ShadowCascadeCount.Three: comboBoxShadowCascades.SelectedIndex = 1; break;
				case ShadowCascadeCount.Four: comboBoxShadowCascades.SelectedIndex = 2; break;
				default: comboBoxShadowCascades.SelectedIndex = 1; break;
			}

			numericUpDownShadowStrength.Value = (decimal)(Interface.CurrentOptions.ShadowStrength * 100.0);
			numericUpDownShadowBias.Value = (decimal)Interface.CurrentOptions.ShadowBias;
			numericUpDownShadowNormalBias.Value = (decimal)Interface.CurrentOptions.ShadowNormalBias;


			// Initialize sun direction sliders from current light position
			InitializeSunSliders();

			// Wire up shadow resolution change to enable/disable related controls
			comboBoxShadowResolution.SelectedIndexChanged += comboBoxShadowResolution_SelectedIndexChanged;
			UpdateShadowControlsEnabled();

			InitializePostUI();

			comboBoxLeft.DataSource = Enum.GetValues(typeof(Key));
			comboBoxLeft.SelectedItem = Interface.CurrentOptions.CameraMoveLeft;
			comboBoxRight.DataSource = Enum.GetValues(typeof(Key));
			comboBoxRight.SelectedItem = Interface.CurrentOptions.CameraMoveRight;
			comboBoxUp.DataSource = Enum.GetValues(typeof(Key));
			comboBoxUp.SelectedItem = Interface.CurrentOptions.CameraMoveUp;
			comboBoxDown.DataSource = Enum.GetValues(typeof(Key));
			comboBoxDown.SelectedItem = Interface.CurrentOptions.CameraMoveDown;
			comboBoxForwards.DataSource = Enum.GetValues(typeof(Key));
			comboBoxForwards.SelectedItem = Interface.CurrentOptions.CameraMoveForward;
			comboBoxBackwards.DataSource = Enum.GetValues(typeof(Key));
			comboBoxBackwards.SelectedItem = Interface.CurrentOptions.CameraMoveBackward;
			checkBoxAutoReload.Checked = Interface.CurrentOptions.AutoReloadObjects;
			checkBoxShadowFilterCascades.Checked = Interface.CurrentOptions.ShadowFilterCascades;

			// VSync and FPS Limit
			comboBoxVSync.SelectedIndex = Interface.CurrentOptions.VerticalSynchronization ? 1 : 0;
			// Map FPSLimit value to combo index: 0=Unlimited, 1=30, 2=60, 3=120, 4=240
			switch (Interface.CurrentOptions.FPSLimit)
			{
				case 30: comboBoxFPSLimit.SelectedIndex = 1; break;
				case 60: comboBoxFPSLimit.SelectedIndex = 2; break;
				case 120: comboBoxFPSLimit.SelectedIndex = 3; break;
				case 240: comboBoxFPSLimit.SelectedIndex = 4; break;
				default: comboBoxFPSLimit.SelectedIndex = 0; break;
			}
			UpdateFPSLimitEnabled();
		}

		private void InitializeSunSliders()
		{
			trackBarSunElevation.Value = Math.Max(trackBarSunElevation.Minimum, Math.Min((int)Interface.CurrentOptions.LightElevation, trackBarSunElevation.Maximum));
			trackBarSunAzimuth.Value = Math.Max(trackBarSunAzimuth.Minimum, Math.Min((int)Interface.CurrentOptions.LightAzimuth, trackBarSunAzimuth.Maximum));
			labelSunAzimuthValue.Text = trackBarSunAzimuth.Value + "\u00b0";
			labelSunElevationValue.Text = trackBarSunElevation.Value + "\u00b0";
		}

		private void UpdateShadowControlsEnabled()
		{
			bool enabled = comboBoxShadowResolution.SelectedIndex != 0; // 0 = Off
			comboBoxShadowDistance.Enabled = enabled;
			comboBoxShadowCascades.Enabled = enabled;
			numericUpDownShadowStrength.Enabled = enabled;
			numericUpDownShadowBias.Enabled = enabled;
			numericUpDownShadowBias.ReadOnly = !enabled;
			numericUpDownShadowNormalBias.Enabled = enabled;
			numericUpDownShadowNormalBias.ReadOnly = !enabled;
			
			checkBoxShadowFilterCascades.Enabled = enabled;
		}

		private void comboBoxShadowResolution_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateShadowControlsEnabled();
		}

			// Viewer slim post UI (master + AoMode + Intensity + Radius), programmatic TabPage.
		private System.Windows.Forms.TabPage tabPagePost;
		private System.Windows.Forms.CheckBox checkBoxPostEnabled;
		private System.Windows.Forms.ComboBox comboBoxAoMode;
		private System.Windows.Forms.NumericUpDown numericAoIntensity;
		private System.Windows.Forms.NumericUpDown numericAoRadius;
		private System.Windows.Forms.Button buttonAoPresetLow;
		private System.Windows.Forms.Button buttonAoPresetBalanced;
		private System.Windows.Forms.Button buttonAoPresetQuality;
		private System.Windows.Forms.Label labelAoPresetCurrent;

		private void InitializePostUI()
		{
			try
			{
				tabPagePost = new System.Windows.Forms.TabPage("Post-Processing");
				var tlp = new System.Windows.Forms.TableLayoutPanel
				{
					AutoSize = true,
					Dock = System.Windows.Forms.DockStyle.Top,
					Padding = new System.Windows.Forms.Padding(10),
					ColumnCount = 2,
					ColumnStyles =
					{
						new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F),
						new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F)
					}
				};
			var labelPostEnabled = new System.Windows.Forms.Label { AutoSize = true, Text = "Enable post-processing:" };
			checkBoxPostEnabled = new System.Windows.Forms.CheckBox { AutoSize = true };
			checkBoxPostEnabled.Checked = Interface.CurrentOptions.EnablePostProcessing;
			checkBoxPostEnabled.CheckedChanged += checkBoxPostEnabled_CheckedChanged;
			var labelAoMode = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Mode:" };
			comboBoxAoMode = new System.Windows.Forms.ComboBox { Dock = System.Windows.Forms.DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
			comboBoxAoMode.Items.AddRange(new object[] { "Off", "SAO", "GTAO" });
			comboBoxAoMode.SelectedIndex = AoModeMapper.ToSelectedIndex(Interface.CurrentOptions.AoMode);
			comboBoxAoMode.SelectedIndexChanged += comboBoxAoMode_SelectedIndexChanged;
			var labelAoIntensity = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Intensity (0-2):" };
			numericAoIntensity = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.1m, Minimum = 0, Maximum = 2 };
			numericAoIntensity.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoIntensity, numericAoIntensity.Minimum, numericAoIntensity.Maximum);
			numericAoIntensity.ValueChanged += PostNumericChanged;
			var labelAoRadius = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Radius m (0.1-5):" };
			numericAoRadius = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.1m, Minimum = 0.1m, Maximum = 5 };
			numericAoRadius.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoRadius, numericAoRadius.Minimum, numericAoRadius.Maximum);
			numericAoRadius.ValueChanged += PostNumericChanged;
			var labelAoPreset = new System.Windows.Forms.Label { AutoSize = true, Text = "Quality preset:" };
			var presetFlow = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
			buttonAoPresetLow = new System.Windows.Forms.Button { AutoSize = true, Text = "Fast" };
			buttonAoPresetLow.Click += buttonAoPresetLow_Click;
			buttonAoPresetBalanced = new System.Windows.Forms.Button { AutoSize = true, Text = "Balanced" };
			buttonAoPresetBalanced.Click += buttonAoPresetBalanced_Click;
			buttonAoPresetQuality = new System.Windows.Forms.Button { AutoSize = true, Text = "Quality" };
			buttonAoPresetQuality.Click += buttonAoPresetQuality_Click;
			labelAoPresetCurrent = new System.Windows.Forms.Label { AutoSize = true };
			presetFlow.Controls.Add(buttonAoPresetLow);
			presetFlow.Controls.Add(buttonAoPresetBalanced);
			presetFlow.Controls.Add(buttonAoPresetQuality);
			presetFlow.Controls.Add(labelAoPresetCurrent);
				tlp.Controls.Add(labelPostEnabled, 0, 0);
				tlp.Controls.Add(checkBoxPostEnabled, 1, 0);
				tlp.Controls.Add(labelAoMode, 0, 1);
				tlp.Controls.Add(comboBoxAoMode, 1, 1);
				tlp.Controls.Add(labelAoIntensity, 0, 2);
				tlp.Controls.Add(numericAoIntensity, 1, 2);
			tlp.Controls.Add(labelAoRadius, 0, 3);
			tlp.Controls.Add(numericAoRadius, 1, 3);
			tlp.Controls.Add(labelAoPreset, 0, 4);
			tlp.Controls.Add(presetFlow, 1, 4);
				tabPagePost.Controls.Add(tlp);
				tabPagePost.AutoScroll = true;
				tabControl1.Controls.Add(tabPagePost);
				UpdatePostControlsEnabled();
			}
			catch
			{
				// ignored: post UI must never break options dialog
			}
		}

		private void UpdatePostControlsEnabled()
		{
			try
			{
				if (comboBoxAoMode == null || checkBoxPostEnabled == null)
				{
					return;
				}
				bool supportsCompute = LibRender2.Menu.MenuBuilder.SupportsCompute(Program.Renderer);
				if (!supportsCompute)
				{
					comboBoxAoMode.Enabled = false;
					if (numericAoIntensity != null) numericAoIntensity.Enabled = false;
					if (numericAoRadius != null) numericAoRadius.Enabled = false;
					toolTip1.SetToolTip(comboBoxAoMode, "Requires OpenGL 4.3");
					toolTip1.SetToolTip(tabPagePost, "Requires OpenGL 4.3");
					return;
				}
		bool master = checkBoxPostEnabled.Checked;
		bool aoOn = AoModeMapper.FromSelectedIndex(comboBoxAoMode.SelectedIndex) != AmbientOcclusionMode.Off;
		if (numericAoIntensity != null) numericAoIntensity.Enabled = master && aoOn;
		if (numericAoRadius != null) numericAoRadius.Enabled = master && aoOn;
		comboBoxAoMode.Enabled = master;
		bool presetOn = master && aoOn;
		if (buttonAoPresetLow != null) buttonAoPresetLow.Enabled = presetOn;
		if (buttonAoPresetBalanced != null) buttonAoPresetBalanced.Enabled = presetOn;
		if (buttonAoPresetQuality != null) buttonAoPresetQuality.Enabled = presetOn;
		if (labelAoPresetCurrent != null)
		{
			int s = Interface.CurrentOptions.SaoSamples;
			int sl = Interface.CurrentOptions.GtaoSlices;
			int st = Interface.CurrentOptions.GtaoSteps;
			string tier = (s == 3 && sl == 3 && st == 2) ? "Fast"
				: (s == 5 && sl == 4 && st == 3) ? "Balanced"
				: (s == 12 && sl == 6 && st == 4) ? "Quality" : "Custom";
			labelAoPresetCurrent.Text = "Current: " + tier + " (SAO " + s + "x2 / GTAO " + sl + "x" + st + ")";
		}
		}
		catch
		{
			// ignored
		}
	}

	// Live post/AO sync (mirrors formMain.Options + MenuOption auto-enable pattern).
	private void checkBoxPostEnabled_CheckedChanged(object sender, EventArgs e)
	{
		try
		{
			Interface.CurrentOptions.EnablePostProcessing = checkBoxPostEnabled.Checked;
			UpdatePostControlsEnabled();
			SyncPostProcessor();
		}
		catch
		{
			// ignored
		}
	}

	private void comboBoxAoMode_SelectedIndexChanged(object sender, EventArgs e)
	{
		try
		{
			Interface.CurrentOptions.AoMode = AoModeMapper.FromSelectedIndex(comboBoxAoMode.SelectedIndex);
			// Selecting SAO/GTAO auto-enables master so the effect is visible immediately.
			if (Interface.CurrentOptions.AoMode != AmbientOcclusionMode.Off && checkBoxPostEnabled != null && !checkBoxPostEnabled.Checked)
			{
				checkBoxPostEnabled.Checked = true;
				Interface.CurrentOptions.EnablePostProcessing = true;
			}
			UpdatePostControlsEnabled();
			SyncPostProcessor();
		}
		catch
		{
			// ignored
		}
	}

	private void PostNumericChanged(object sender, EventArgs e)
	{
		try
		{
			if (numericAoIntensity != null)
			{
				Interface.CurrentOptions.AoIntensity = (float)AoLimits.Clamp(numericAoIntensity.Value, 0, 2);
			}
			if (numericAoRadius != null)
			{
				Interface.CurrentOptions.AoRadius = (float)AoLimits.Clamp(numericAoRadius.Value, 0.1m, 5);
			}
			SyncPostProcessor();
		}
		catch
		{
			// ignored
		}
	}

	// Quality presets mirror the main app (Fast/Balanced/Quality sample counts).
	private void buttonAoPresetLow_Click(object sender, EventArgs e)
	{
		ApplyAoPreset(3, 3, 2);
	}

	private void buttonAoPresetBalanced_Click(object sender, EventArgs e)
	{
		ApplyAoPreset(5, 4, 3);
	}

	private void buttonAoPresetQuality_Click(object sender, EventArgs e)
	{
		ApplyAoPreset(12, 6, 4);
	}

	private void ApplyAoPreset(int saoSamples, int gtaoSlices, int gtaoSteps)
	{
		try
		{
			Interface.CurrentOptions.SaoSamples = saoSamples;
			Interface.CurrentOptions.GtaoSlices = gtaoSlices;
			Interface.CurrentOptions.GtaoSteps = gtaoSteps;
			UpdatePostControlsEnabled();
			SyncPostProcessor();
		}
		catch
		{
			// ignored
		}
	}

	private void SyncPostProcessor()
	{
		try
		{
			if (Program.Renderer != null && Program.Renderer.PostProcessor != null)
			{
				Program.Renderer.PostProcessor.SyncFromOptions(Interface.CurrentOptions);
			}
		}
		catch
		{
			// ignored: live sync must never break the dialog
		}
	}

		private void UpdateSunDirection()
		{
			Interface.CurrentOptions.LightAzimuth = trackBarSunAzimuth.Value;
			Interface.CurrentOptions.LightElevation = trackBarSunElevation.Value;

			double azimuthRad = Interface.CurrentOptions.LightAzimuth * Math.PI / 180.0;
			double elevationRad = Interface.CurrentOptions.LightElevation * Math.PI / 180.0;

			// Convert spherical to direction vector (matching DirectionalLight docs)
			float x = (float)(-Math.Cos(elevationRad) * Math.Sin(azimuthRad));
			float y = (float)Math.Sin(elevationRad);
			float z = (float)(-Math.Cos(elevationRad) * Math.Cos(azimuthRad));

			Program.Renderer.Lighting.OptionLightPosition = new Vector3(x, y, z);
		}

		private void UpdateFPSLimitEnabled()
		{
			bool vsyncEnabled = comboBoxVSync.SelectedIndex == 1;
			comboBoxFPSLimit.Enabled = !vsyncEnabled;
			labelFPSLimit.ForeColor = vsyncEnabled ? System.Drawing.SystemColors.GrayText : System.Drawing.SystemColors.ControlText;
		}

		private void comboBoxVSync_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateFPSLimitEnabled();
		}

		private void trackBarSunAzimuth_Scroll(object sender, EventArgs e)
		{
			labelSunAzimuthValue.Text = trackBarSunAzimuth.Value + "\u00b0";
			UpdateSunDirection();
		}

		private void trackBarSunElevation_Scroll(object sender, EventArgs e)
		{
			labelSunElevationValue.Text = trackBarSunElevation.Value + "\u00b0";
			UpdateSunDirection();
		}

		internal static DialogResult ShowOptions()
		{
			formOptions Dialog = new formOptions();
			DialogResult Result = Dialog.ShowDialog();
			return Result;
		}

		private void CloseButton_Click(object sender, EventArgs e)
		{
			int previousAntialiasingLevel = Interface.CurrentOptions.AntiAliasingLevel;

			//Interpolation mode
			InterpolationMode previousInterpolationMode = Interface.CurrentOptions.Interpolation;
			switch (InterpolationMode.SelectedIndex)
			{
				case 0:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.NearestNeighbor;
					break;
				case 1:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.Bilinear;
					break;
				case 2:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.NearestNeighborMipmapped;
					break;
				case 3:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.BilinearMipmapped;
					break;
				case 4:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.TrilinearMipmapped;
					break;
				case 5:
					Interface.CurrentOptions.Interpolation = OpenBveApi.Graphics.InterpolationMode.AnisotropicFiltering;
					break;
			}

			if (previousInterpolationMode != Interface.CurrentOptions.Interpolation)
			{
				// We have changed interpolation level, so the texture cache needs totally clearing (as opposed to changed files)
				Program.Renderer.TextureManager.UnloadAllTextures(false);
			}

			//Anisotropic filtering level
			Interface.CurrentOptions.AnisotropicFilteringLevel = (int) AnisotropicLevel.Value;
			//Antialiasing level
			Interface.CurrentOptions.AntiAliasingLevel = (int) AntialiasingLevel.Value;
			if (Interface.CurrentOptions.AntiAliasingLevel != previousAntialiasingLevel)
			{
				Program.Renderer.GraphicsMode = new GraphicsMode(new ColorFormat(8, 8, 8, 8), 24, 8, Interface.CurrentOptions.AntiAliasingLevel);
			}

			//Transparency quality
			switch (TransparencyQuality.SelectedIndex)
			{
				case 0:
					Interface.CurrentOptions.TransparencyMode = TransparencyMode.Performance;
					break;
				default:
					Interface.CurrentOptions.TransparencyMode = TransparencyMode.Quality;
					break;
			}

			//Set width and height
			if (Program.Renderer.Screen.Width != width.Value || Program.Renderer.Screen.Height != height.Value)
			{
				if (width.Value > 300 && height.Value > 300)
				{
					Program.Renderer.SetWindowSize((int)width.Value, (int)height.Value);
					Program.Renderer.UpdateViewport(ViewportChangeMode.NoChange);
				}
			}

			XParsers xParser = (XParsers)comboBoxNewXParser.SelectedIndex;
			ObjParsers objParser = (ObjParsers)comboBoxNewObjParser.SelectedIndex;

			if (Interface.CurrentOptions.CurrentXParser != xParser || Interface.CurrentOptions.CurrentObjParser != objParser)
			{
				Interface.CurrentOptions.CurrentXParser = xParser;
				Interface.CurrentOptions.CurrentObjParser = objParser;
				Program.CurrentHost.StaticObjectCache.Clear(); // as a different parser may interpret differently
				for (int i = 0; i < Program.CurrentHost.Plugins.Length; i++)
				{
					if (Program.CurrentHost.Plugins[i].Object != null)
					{
						Program.CurrentHost.Plugins[i].Object.SetObjectParser(Interface.CurrentOptions.CurrentXParser);
						Program.CurrentHost.Plugins[i].Object.SetObjectParser(Interface.CurrentOptions.CurrentObjParser);
					}
				}
			}
			

			Interface.CurrentOptions.ObjectOptimizationMode = (ObjectOptimizationMode)comboBoxOptimizeObjects.SelectedIndex;
			Interface.CurrentOptions.CameraMoveLeft = (Key)comboBoxLeft.SelectedItem;
			Interface.CurrentOptions.CameraMoveRight = (Key)comboBoxRight.SelectedItem;
			Interface.CurrentOptions.CameraMoveUp = (Key)comboBoxUp.SelectedItem;
			Interface.CurrentOptions.CameraMoveDown = (Key)comboBoxDown.SelectedItem;
			Interface.CurrentOptions.CameraMoveForward = (Key)comboBoxForwards.SelectedItem;
			Interface.CurrentOptions.CameraMoveBackward = (Key)comboBoxBackwards.SelectedItem;
			Interface.CurrentOptions.NearClipBase = (double)nearClip.Value;
			// ensure viewing distance is greater than the near clipping plane to avoid rendering issues
			if (Interface.CurrentOptions.ViewingDistance <= Interface.CurrentOptions.NearClipBase)
			{
				Interface.CurrentOptions.ViewingDistance = (int)Math.Ceiling(Interface.CurrentOptions.NearClipBase) + 1;
			}
			Interface.CurrentOptions.AutoReloadObjects = checkBoxAutoReload.Checked;

			// VSync and FPS Limit
			Interface.CurrentOptions.VerticalSynchronization = comboBoxVSync.SelectedIndex == 1;
			// Map combo index to FPSLimit value
			int[] fpsPresets = { 0, 30, 60, 120, 240 };
			Interface.CurrentOptions.FPSLimit = comboBoxFPSLimit.SelectedIndex >= 0 ? fpsPresets[comboBoxFPSLimit.SelectedIndex] : 0;
			Program.Renderer.GameWindow.VSync = Interface.CurrentOptions.VerticalSynchronization ? OpenTK.VSyncMode.On : OpenTK.VSyncMode.Off;
			Program.Renderer.GameWindow.TargetRenderFrequency = Interface.CurrentOptions.FPSLimit > 0 ? Interface.CurrentOptions.FPSLimit : 0;

			// Saving shadow settings
			switch (comboBoxShadowResolution.SelectedIndex)
			{
				case 0: Interface.CurrentOptions.ShadowResolution = ShadowMapResolution.Off; break;
				case 1: Interface.CurrentOptions.ShadowResolution = ShadowMapResolution.Low; break;
				case 2: Interface.CurrentOptions.ShadowResolution = ShadowMapResolution.Medium; break;
				case 3: Interface.CurrentOptions.ShadowResolution = ShadowMapResolution.High; break;
				case 4: Interface.CurrentOptions.ShadowResolution = ShadowMapResolution.Ultra; break;
			}

			switch (comboBoxShadowDistance.SelectedIndex)
			{
				case 0: Interface.CurrentOptions.ShadowDrawDistance = ShadowDistance.Near; break;
				case 1: Interface.CurrentOptions.ShadowDrawDistance = ShadowDistance.Medium; break;
				case 2: Interface.CurrentOptions.ShadowDrawDistance = ShadowDistance.Far; break;
				case 3: Interface.CurrentOptions.ShadowDrawDistance = ShadowDistance.VeryFar; break;
				case 4: Interface.CurrentOptions.ShadowDrawDistance = ShadowDistance.ViewingDistance; break;
			}

			switch (comboBoxShadowCascades.SelectedIndex)
			{
				case 0: Interface.CurrentOptions.ShadowCascades = ShadowCascadeCount.Two; break;
				case 1: Interface.CurrentOptions.ShadowCascades = ShadowCascadeCount.Three; break;
				case 2: Interface.CurrentOptions.ShadowCascades = ShadowCascadeCount.Four; break;
			}

			Interface.CurrentOptions.ShadowStrength = (double)numericUpDownShadowStrength.Value / 100.0;
			Interface.CurrentOptions.ShadowBias = (double)numericUpDownShadowBias.Value;
			Interface.CurrentOptions.ShadowNormalBias = (double)numericUpDownShadowNormalBias.Value;
			Interface.CurrentOptions.ShadowFilterCascades = checkBoxShadowFilterCascades.Checked;

			// Viewer slim post save (order stays via CSV)
			try
			{
				if (checkBoxPostEnabled != null)
				{
					Interface.CurrentOptions.EnablePostProcessing = checkBoxPostEnabled.Checked;
				}
				if (comboBoxAoMode != null)
				{
					Interface.CurrentOptions.AoMode = AoModeMapper.FromSelectedIndex(comboBoxAoMode.SelectedIndex);
				}
				if (numericAoIntensity != null)
				{
					Interface.CurrentOptions.AoIntensity = (float)AoLimits.Clamp(numericAoIntensity.Value, 0, 2);
				}
				if (numericAoRadius != null)
				{
					Interface.CurrentOptions.AoRadius = (float)AoLimits.Clamp(numericAoRadius.Value, 0.1m, 5);
				}
			}
			catch
			{
				// ignored
			}
			
			Interface.CurrentOptions.Save(Path.CombineFile(Program.FileSystem.SettingsFolder, "1.5.0/options_ov.cfg"));
			Program.RefreshObjects();
			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
