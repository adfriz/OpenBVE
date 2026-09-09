using LibRender2.Viewports;
using OpenBveApi;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Objects;
using OpenTK.Graphics;
using System;
using System.ComponentModel;
using System.Windows.Forms;
using OpenBveApi.Math;

namespace RouteViewer
{
    public partial class FormOptions : Form
    {
        public FormOptions()
        {
            InitializeComponent();
            InterpolationMode.SelectedIndex = (int) Interface.CurrentOptions.Interpolation;
            AnisotropicLevel.Value = Interface.CurrentOptions.AnisotropicFilteringLevel;
            AntialiasingLevel.Value = Interface.CurrentOptions.AntiAliasingLevel;
            TransparencyQuality.SelectedIndex = Interface.CurrentOptions.TransparencyMode == TransparencyMode.Performance ? 0 : 2;
            width.Value = Program.Renderer.Screen.Width;
            height.Value = Program.Renderer.Screen.Height;
			checkBoxLogo.Checked = Interface.CurrentOptions.LoadingLogo;
			checkBoxBackgrounds.Checked = Interface.CurrentOptions.LoadingBackground;
			checkBoxProgressBar.Checked = Interface.CurrentOptions.LoadingProgressBar;
			comboBoxNewXParser.SelectedIndex = (int) Interface.CurrentOptions.CurrentXParser;
			comboBoxNewObjParser.SelectedIndex = (int) Interface.CurrentOptions.CurrentObjParser;
			comboBoxOptimizeObjects.SelectedIndex = (int)Interface.CurrentOptions.ObjectOptimizationMode;
			numericUpDownViewingDistance.Value = Math.Min(Interface.CurrentOptions.ViewingDistance, numericUpDownViewingDistance.Maximum);

            // Shadows
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

            numericUpDownShadowStrength.Minimum = 1;
            numericUpDownShadowStrength.Maximum = 100;
            numericUpDownShadowStrength.Increment = 5;
            numericUpDownShadowStrength.DecimalPlaces = 0;
            numericUpDownShadowStrength.Value = (decimal)Math.Round(Interface.CurrentOptions.ShadowStrength * 100.0);
            if (numericUpDownShadowStrength.Value < 1) numericUpDownShadowStrength.Value = 1;
            numericUpDownShadowStrength.Refresh();
            numericUpDownShadowBias.Value = (decimal)Interface.CurrentOptions.ShadowBias;
            numericUpDownShadowBias.Refresh();

            numericUpDownShadowNormalBias.DecimalPlaces = 2;
            numericUpDownShadowNormalBias.Minimum = 0;
            numericUpDownShadowNormalBias.Maximum = 10;
            numericUpDownShadowNormalBias.Increment = 0.1m;
            numericUpDownShadowNormalBias.Value = (decimal)Interface.CurrentOptions.ShadowNormalBias;
            numericUpDownShadowNormalBias.Refresh();


            // Initialize sun direction sliders from current light position
            InitializeSunSliders();

            // Wire up shadow resolution change to enable/disable related controls
            comboBoxShadowResolution.SelectedIndexChanged += comboBoxShadowResolution_SelectedIndexChanged;
            UpdateShadowControlsEnabled();
			InitializePostUI();
			numericUpDownViewingDistance.Value = Math.Min(Interface.CurrentOptions.ViewingDistance, numericUpDownViewingDistance.Maximum);
			numericUpDownNearClip.Value = (decimal)Interface.CurrentOptions.NearClipBase;
			if (Translations.CurrentLanguageCode != "en-US")
			{
				labelNearClip.Text = Translations.GetInterfaceString(OpenBveApi.Hosts.HostApplication.OpenBve, new[] { "options", "quality_distance_nearclip" });
			}
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
            bool enabled = comboBoxShadowResolution.SelectedIndex != 0;
            comboBoxShadowDistance.Enabled = enabled;
            comboBoxShadowCascades.Enabled = enabled;
            numericUpDownShadowStrength.Enabled = enabled;
            numericUpDownShadowBias.Enabled = enabled;
            numericUpDownShadowBias.ReadOnly = !enabled;
            numericUpDownShadowNormalBias.Enabled = enabled;
            numericUpDownShadowNormalBias.ReadOnly = !enabled;

            trackBarSunAzimuth.Enabled = enabled;
            trackBarSunElevation.Enabled = enabled;
            checkBoxShadowFilterCascades.Enabled = enabled;
        }

        private void comboBoxShadowResolution_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateShadowControlsEnabled();
        }

		// Viewer post UI: master on top + nested per-effect tabs (AO | FXAA | Sharpen | Vignette).
		// AO page uses GroupBoxes (Common / SAO-only / GTAO-only / Quality preset).
		// Retired keys (AoBias, SaoSpiralTurns, AoBlurSharpness) have no UI: no uniform reads them.
		// FXAA/Sharpen/Vignette are on/off only (SinglePassEffect bakes intensity; no tunable params exist).
		private System.Windows.Forms.TabPage tabPagePost;
		private System.Windows.Forms.CheckBox checkBoxPostEnabled;
		private System.Windows.Forms.ComboBox comboBoxAoMode;
		private System.Windows.Forms.NumericUpDown numericAoIntensity;
		private System.Windows.Forms.NumericUpDown numericAoRadius;
		private System.Windows.Forms.NumericUpDown numericAoPower;
		private System.Windows.Forms.ComboBox comboAoResolution;
		private System.Windows.Forms.NumericUpDown numericAoBlurRadius;
		private System.Windows.Forms.CheckBox checkBoxAoDebugView;
		private System.Windows.Forms.NumericUpDown numericSaoSamples;
		private System.Windows.Forms.NumericUpDown numericAoHorizon;
		private System.Windows.Forms.NumericUpDown numericAoDetail;
		private System.Windows.Forms.NumericUpDown numericGtaoSlices;
		private System.Windows.Forms.NumericUpDown numericGtaoSteps;
		private System.Windows.Forms.NumericUpDown numericGtaoFalloff;
		private System.Windows.Forms.Button buttonAoPresetLow;
		private System.Windows.Forms.Button buttonAoPresetBalanced;
		private System.Windows.Forms.Button buttonAoPresetQuality;
		private System.Windows.Forms.Label labelAoPresetCurrent;
		private System.Windows.Forms.CheckBox checkBoxPostFxaa;
		private System.Windows.Forms.CheckBox checkBoxPostSharpen;
		private System.Windows.Forms.CheckBox checkBoxPostVignette;
		// Built-in AA level parked while FXAA is on (FXAA on = scene bypasses MSAA anyway).
		private int? aaLevelBeforeFxaa = null;

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
			tlp.Controls.Add(labelPostEnabled, 0, 0);
			tlp.Controls.Add(checkBoxPostEnabled, 1, 0);
			tlp.Controls.Add(labelAoMode, 0, 1);
			tlp.Controls.Add(comboBoxAoMode, 1, 1);
		// Nested tabs: master + mode on top, TabControl below (Common | SAO only | GTAO only | Preset).
		var labelAoIntensity = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Intensity (0-2):" };
		numericAoIntensity = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.1m, Minimum = 0, Maximum = 2 };
		numericAoIntensity.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoIntensity, numericAoIntensity.Minimum, numericAoIntensity.Maximum);
		numericAoIntensity.ValueChanged += PostValueChanged;
		var labelAoRadius = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Radius m (0.1-5):" };
		numericAoRadius = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.1m, Minimum = 0.1m, Maximum = 5 };
		numericAoRadius.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoRadius, numericAoRadius.Minimum, numericAoRadius.Maximum);
		numericAoRadius.ValueChanged += PostValueChanged;
		var labelAoPower = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Power (0.5-3):" };
		numericAoPower = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.5m, Maximum = 3 };
		numericAoPower.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoPower, numericAoPower.Minimum, numericAoPower.Maximum);
		numericAoPower.ValueChanged += PostValueChanged;
		var labelAoResolution = new System.Windows.Forms.Label { AutoSize = true, Text = "AO Resolution scale:" };
		comboAoResolution = new System.Windows.Forms.ComboBox { Dock = System.Windows.Forms.DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
		comboAoResolution.Items.AddRange(new object[] { "0.25", "0.5", "1.0" });
		float rsInit = Interface.CurrentOptions.AoResolutionScale;
		comboAoResolution.SelectedIndex = rsInit <= 0.251f ? 0 : rsInit >= 0.99f ? 2 : 1;
		comboAoResolution.SelectedIndexChanged += PostValueChanged;
		var labelAoBlurRadius = new System.Windows.Forms.Label { AutoSize = true, Text = "Denoise blur radius (0-8):" };
		numericAoBlurRadius = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 8 };
		numericAoBlurRadius.Value = AoLimits.Clamp(Interface.CurrentOptions.AoBlurRadius, (int)numericAoBlurRadius.Minimum, (int)numericAoBlurRadius.Maximum);
		numericAoBlurRadius.ValueChanged += PostValueChanged;
		var labelAoDebugView = new System.Windows.Forms.Label { AutoSize = true, Text = "AO-only debug view:" };
		checkBoxAoDebugView = new System.Windows.Forms.CheckBox { AutoSize = true };
		checkBoxAoDebugView.Checked = Interface.CurrentOptions.AoDebugView != 0;
		checkBoxAoDebugView.CheckedChanged += PostValueChanged;
		var gridCommon = NewPostGrid();
		gridCommon.Controls.Add(labelAoIntensity, 0, 0);
		gridCommon.Controls.Add(numericAoIntensity, 1, 0);
		gridCommon.Controls.Add(labelAoRadius, 0, 1);
		gridCommon.Controls.Add(numericAoRadius, 1, 1);
		gridCommon.Controls.Add(labelAoPower, 0, 2);
		gridCommon.Controls.Add(numericAoPower, 1, 2);
		gridCommon.Controls.Add(labelAoResolution, 0, 3);
		gridCommon.Controls.Add(comboAoResolution, 1, 3);
		gridCommon.Controls.Add(labelAoBlurRadius, 0, 4);
		gridCommon.Controls.Add(numericAoBlurRadius, 1, 4);
		gridCommon.Controls.Add(labelAoDebugView, 0, 5);
		gridCommon.Controls.Add(checkBoxAoDebugView, 1, 5);
		var labelSaoSamples = new System.Windows.Forms.Label { AutoSize = true, Text = "Pattern taps (1-32):" };
		numericSaoSamples = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 0, Increment = 1, Minimum = 1, Maximum = 32 };
		numericSaoSamples.Value = AoLimits.Clamp(Interface.CurrentOptions.SaoSamples, (int)numericSaoSamples.Minimum, (int)numericSaoSamples.Maximum);
		numericSaoSamples.ValueChanged += PostValueChanged;
		var labelAoHorizon = new System.Windows.Forms.Label { AutoSize = true, Text = "Horizon threshold (0-0.2):" };
		numericAoHorizon = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 3, Increment = 0.005m, Minimum = 0, Maximum = 0.2m };
		numericAoHorizon.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoHorizonThreshold, numericAoHorizon.Minimum, numericAoHorizon.Maximum);
		numericAoHorizon.ValueChanged += PostValueChanged;
		var labelAoDetail = new System.Windows.Forms.Label { AutoSize = true, Text = "Detail strength (0-5):" };
		numericAoDetail = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.1m, Minimum = 0, Maximum = 5 };
		numericAoDetail.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoDetailStrength, numericAoDetail.Minimum, numericAoDetail.Maximum);
		numericAoDetail.ValueChanged += PostValueChanged;
		var gridSao = NewPostGrid();
		gridSao.Controls.Add(labelSaoSamples, 0, 0);
		gridSao.Controls.Add(numericSaoSamples, 1, 0);
		gridSao.Controls.Add(labelAoHorizon, 0, 1);
		gridSao.Controls.Add(numericAoHorizon, 1, 1);
		gridSao.Controls.Add(labelAoDetail, 0, 2);
		gridSao.Controls.Add(numericAoDetail, 1, 2);
		var labelGtaoSlices = new System.Windows.Forms.Label { AutoSize = true, Text = "Slices (1-8):" };
		numericGtaoSlices = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 0, Increment = 1, Minimum = 1, Maximum = 8 };
		numericGtaoSlices.Value = AoLimits.Clamp(Interface.CurrentOptions.GtaoSlices, (int)numericGtaoSlices.Minimum, (int)numericGtaoSlices.Maximum);
		numericGtaoSlices.ValueChanged += PostValueChanged;
		var labelGtaoSteps = new System.Windows.Forms.Label { AutoSize = true, Text = "Steps (1-8):" };
		numericGtaoSteps = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 0, Increment = 1, Minimum = 1, Maximum = 8 };
		numericGtaoSteps.Value = AoLimits.Clamp(Interface.CurrentOptions.GtaoSteps, (int)numericGtaoSteps.Minimum, (int)numericGtaoSteps.Maximum);
		numericGtaoSteps.ValueChanged += PostValueChanged;
		var labelGtaoFalloff = new System.Windows.Forms.Label { AutoSize = true, Text = "Falloff range (0.05-2):" };
		numericGtaoFalloff = new System.Windows.Forms.NumericUpDown { Dock = System.Windows.Forms.DockStyle.Fill, DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.05m, Maximum = 2 };
		numericGtaoFalloff.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.GtaoFalloffRange, numericGtaoFalloff.Minimum, numericGtaoFalloff.Maximum);
		numericGtaoFalloff.ValueChanged += PostValueChanged;
		var gridGtao = NewPostGrid();
		gridGtao.Controls.Add(labelGtaoSlices, 0, 0);
		gridGtao.Controls.Add(numericGtaoSlices, 1, 0);
		gridGtao.Controls.Add(labelGtaoSteps, 0, 1);
		gridGtao.Controls.Add(numericGtaoSteps, 1, 1);
		gridGtao.Controls.Add(labelGtaoFalloff, 0, 2);
		gridGtao.Controls.Add(numericGtaoFalloff, 1, 2);
		var labelAoPreset = new System.Windows.Forms.Label { AutoSize = true, Text = "Quality preset (sets SAO + GTAO tiers):" };
		var presetFlow = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
		buttonAoPresetLow = new System.Windows.Forms.Button { AutoSize = true, Text = "Fast" };
		buttonAoPresetLow.Click += buttonAoPresetLow_Click;
		buttonAoPresetBalanced = new System.Windows.Forms.Button { AutoSize = true, Text = "Balanced" };
		buttonAoPresetBalanced.Click += buttonAoPresetBalanced_Click;
		buttonAoPresetQuality = new System.Windows.Forms.Button { AutoSize = true, Text = "Quality" };
		buttonAoPresetQuality.Click += buttonAoPresetQuality_Click;
		presetFlow.Controls.Add(buttonAoPresetLow);
		presetFlow.Controls.Add(buttonAoPresetBalanced);
		presetFlow.Controls.Add(buttonAoPresetQuality);
		labelAoPresetCurrent = new System.Windows.Forms.Label { AutoSize = true };
		var gridPreset = NewPostGrid();
		gridPreset.Controls.Add(labelAoPreset, 0, 0);
		gridPreset.Controls.Add(presetFlow, 1, 0);
		gridPreset.Controls.Add(labelAoPresetCurrent, 0, 1);
		gridPreset.SetColumnSpan(labelAoPresetCurrent, 2);
		// Per-effect sub-tabs: AO page groups its grids; FXAA/Sharpen/Vignette are on/off only.
		var groupCommon = new System.Windows.Forms.GroupBox { Text = "Common (SAO + GTAO)", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
		groupCommon.Controls.Add(gridCommon);
		var groupSao = new System.Windows.Forms.GroupBox { Text = "SAO only", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
		groupSao.Controls.Add(gridSao);
		var groupGtao = new System.Windows.Forms.GroupBox { Text = "GTAO only", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
		groupGtao.Controls.Add(gridGtao);
		var groupPreset = new System.Windows.Forms.GroupBox { Text = "Quality preset", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
		groupPreset.Controls.Add(gridPreset);
		var aoStack = new System.Windows.Forms.TableLayoutPanel
		{
			AutoSize = true,
			Dock = System.Windows.Forms.DockStyle.Top,
			ColumnCount = 1,
			RowCount = 4
		};
		aoStack.Controls.Add(groupCommon, 0, 0);
		aoStack.Controls.Add(groupSao, 0, 1);
		aoStack.Controls.Add(groupGtao, 0, 2);
		aoStack.Controls.Add(groupPreset, 0, 3);
		var pageAo = new System.Windows.Forms.TabPage("AO") { AutoScroll = true };
		pageAo.Controls.Add(aoStack);
		checkBoxPostFxaa = new System.Windows.Forms.CheckBox { AutoSize = true, Text = "Enable FXAA" };
		checkBoxPostFxaa.Checked = Interface.CurrentOptions.PostFxaa;
		checkBoxPostFxaa.CheckedChanged += checkBoxPostFxaa_CheckedChanged;
		var gridFxaa = NewPostGrid();
		gridFxaa.Controls.Add(checkBoxPostFxaa, 0, 0);
		gridFxaa.SetColumnSpan(checkBoxPostFxaa, 2);
		var labelFxaaAa = new System.Windows.Forms.Label { AutoSize = true, Text = "While on, built-in anti-aliasing is parked at 0 (it can't smooth the 3D scene anyway); restored when FXAA is turned off." };
		gridFxaa.Controls.Add(labelFxaaAa, 0, 1);
		gridFxaa.SetColumnSpan(labelFxaaAa, 2);
		var pageFxaa = new System.Windows.Forms.TabPage("FXAA") { AutoScroll = true };
		pageFxaa.Controls.Add(gridFxaa);
		checkBoxPostSharpen = new System.Windows.Forms.CheckBox { AutoSize = true, Text = "Enable Sharpen" };
		checkBoxPostSharpen.Checked = Interface.CurrentOptions.PostSharpen;
		checkBoxPostSharpen.CheckedChanged += PostValueChanged;
		var gridSharpen = NewPostGrid();
		gridSharpen.Controls.Add(checkBoxPostSharpen, 0, 0);
		gridSharpen.SetColumnSpan(checkBoxPostSharpen, 2);
		var pageSharpen = new System.Windows.Forms.TabPage("Sharpen") { AutoScroll = true };
		pageSharpen.Controls.Add(gridSharpen);
		checkBoxPostVignette = new System.Windows.Forms.CheckBox { AutoSize = true, Text = "Enable Vignette" };
		checkBoxPostVignette.Checked = Interface.CurrentOptions.PostVignette;
		checkBoxPostVignette.CheckedChanged += PostValueChanged;
		var gridVignette = NewPostGrid();
		gridVignette.Controls.Add(checkBoxPostVignette, 0, 0);
		gridVignette.SetColumnSpan(checkBoxPostVignette, 2);
		var pageVignette = new System.Windows.Forms.TabPage("Vignette") { AutoScroll = true };
		pageVignette.Controls.Add(gridVignette);
		var innerTabs = new System.Windows.Forms.TabControl { Dock = System.Windows.Forms.DockStyle.Fill };
		innerTabs.TabPages.Add(pageAo);
		innerTabs.TabPages.Add(pageFxaa);
		innerTabs.TabPages.Add(pageSharpen);
		innerTabs.TabPages.Add(pageVignette);
		tabPagePost.Controls.Add(innerTabs);
		tabPagePost.Controls.Add(tlp);
				tabPagePost.Controls.Add(tlp);
				tabPagePost.AutoScroll = true;
				tabControl1.Controls.Add(tabPagePost);
				UpdatePostControlsEnabled();
			}
			catch
			{
				// ignored
			}
		}

		private static System.Windows.Forms.TableLayoutPanel NewPostGrid()
		{
			return new System.Windows.Forms.TableLayoutPanel
			{
				AutoSize = true,
				Dock = System.Windows.Forms.DockStyle.Top,
				Padding = new System.Windows.Forms.Padding(6),
				ColumnCount = 2,
				ColumnStyles =
				{
					new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F),
					new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F)
				}
			};
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
				if (numericAoPower != null) numericAoPower.Enabled = false;
				if (comboAoResolution != null) comboAoResolution.Enabled = false;
				if (numericAoBlurRadius != null) numericAoBlurRadius.Enabled = false;
				if (checkBoxAoDebugView != null) checkBoxAoDebugView.Enabled = false;
				if (numericSaoSamples != null) numericSaoSamples.Enabled = false;
				if (numericAoHorizon != null) numericAoHorizon.Enabled = false;
				if (numericAoDetail != null) numericAoDetail.Enabled = false;
				if (numericGtaoSlices != null) numericGtaoSlices.Enabled = false;
				if (numericGtaoSteps != null) numericGtaoSteps.Enabled = false;
				if (numericGtaoFalloff != null) numericGtaoFalloff.Enabled = false;
				if (buttonAoPresetLow != null) buttonAoPresetLow.Enabled = false;
				if (buttonAoPresetBalanced != null) buttonAoPresetBalanced.Enabled = false;
				if (buttonAoPresetQuality != null) buttonAoPresetQuality.Enabled = false;
				// Fragment effects stay usable without compute (GL 3.3 fallback path).
				if (checkBoxPostFxaa != null) checkBoxPostFxaa.Enabled = checkBoxPostEnabled.Checked;
				if (checkBoxPostSharpen != null) checkBoxPostSharpen.Enabled = checkBoxPostEnabled.Checked;
				if (checkBoxPostVignette != null) checkBoxPostVignette.Enabled = checkBoxPostEnabled.Checked;
				toolTip1.SetToolTip(comboBoxAoMode, "Requires OpenGL 4.3");
				toolTip1.SetToolTip(tabPagePost, "Requires OpenGL 4.3");
				return;
			}
		bool master = checkBoxPostEnabled.Checked;
		bool aoOn = AoModeMapper.FromSelectedIndex(comboBoxAoMode.SelectedIndex) != AmbientOcclusionMode.Off;
		comboBoxAoMode.Enabled = master;
		// Common rows: any AO mode. SAO/GTAO rows gate per mode so affinity is visible.
		AmbientOcclusionMode aoMode = AoModeMapper.FromSelectedIndex(comboBoxAoMode.SelectedIndex);
		bool isSao = master && aoMode == AmbientOcclusionMode.SAO;
		bool isGtao = master && aoMode == AmbientOcclusionMode.GTAO;
		if (numericAoIntensity != null) numericAoIntensity.Enabled = master && aoOn;
		if (numericAoRadius != null) numericAoRadius.Enabled = master && aoOn;
		if (numericAoPower != null) numericAoPower.Enabled = master && aoOn;
		if (comboAoResolution != null) comboAoResolution.Enabled = master && aoOn;
		if (numericAoBlurRadius != null) numericAoBlurRadius.Enabled = master && aoOn;
		if (checkBoxAoDebugView != null) checkBoxAoDebugView.Enabled = master && aoOn;
		if (numericSaoSamples != null) numericSaoSamples.Enabled = isSao;
		if (numericAoHorizon != null) numericAoHorizon.Enabled = isSao;
		if (numericAoDetail != null) numericAoDetail.Enabled = isSao;
		if (numericGtaoSlices != null) numericGtaoSlices.Enabled = isGtao;
		if (numericGtaoSteps != null) numericGtaoSteps.Enabled = isGtao;
		if (numericGtaoFalloff != null) numericGtaoFalloff.Enabled = isGtao;
		if (checkBoxPostFxaa != null) checkBoxPostFxaa.Enabled = master;
		if (checkBoxPostSharpen != null) checkBoxPostSharpen.Enabled = master;
		if (checkBoxPostVignette != null) checkBoxPostVignette.Enabled = master;
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

	// Live sync for all nested-tab rows.
	private void PostValueChanged(object sender, EventArgs e)
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
			if (numericAoPower != null)
			{
				Interface.CurrentOptions.AoPower = (float)AoLimits.Clamp(numericAoPower.Value, 0.5m, 3);
			}
			if (comboAoResolution != null && comboAoResolution.SelectedIndex >= 0)
			{
				Interface.CurrentOptions.AoResolutionScale = comboAoResolution.SelectedIndex == 0 ? 0.25f : comboAoResolution.SelectedIndex == 2 ? 1.0f : 0.5f;
			}
			if (numericAoBlurRadius != null)
			{
				Interface.CurrentOptions.AoBlurRadius = (int)AoLimits.Clamp(numericAoBlurRadius.Value, 0, 8);
			}
			if (checkBoxAoDebugView != null)
			{
				Interface.CurrentOptions.AoDebugView = checkBoxAoDebugView.Checked ? 1 : 0;
			}
			if (numericSaoSamples != null)
			{
				Interface.CurrentOptions.SaoSamples = (int)AoLimits.Clamp(numericSaoSamples.Value, 1, 32);
			}
			if (numericAoHorizon != null)
			{
				Interface.CurrentOptions.AoHorizonThreshold = (float)AoLimits.Clamp(numericAoHorizon.Value, 0, 0.2m);
			}
			if (numericAoDetail != null)
			{
				Interface.CurrentOptions.AoDetailStrength = (float)AoLimits.Clamp(numericAoDetail.Value, 0, 5);
			}
			if (numericGtaoSlices != null)
			{
				Interface.CurrentOptions.GtaoSlices = (int)AoLimits.Clamp(numericGtaoSlices.Value, 1, 8);
			}
			if (numericGtaoSteps != null)
			{
				Interface.CurrentOptions.GtaoSteps = (int)AoLimits.Clamp(numericGtaoSteps.Value, 1, 8);
			}
			if (numericGtaoFalloff != null)
			{
				Interface.CurrentOptions.GtaoFalloffRange = (float)AoLimits.Clamp(numericGtaoFalloff.Value, 0.05m, 2);
			}
			if (checkBoxPostFxaa != null)
			{
				Interface.CurrentOptions.PostFxaa = checkBoxPostFxaa.Checked;
			}
			if (checkBoxPostSharpen != null)
			{
				Interface.CurrentOptions.PostSharpen = checkBoxPostSharpen.Checked;
			}
			if (checkBoxPostVignette != null)
			{
				Interface.CurrentOptions.PostVignette = checkBoxPostVignette.Checked;
			}
			UpdatePostControlsEnabled();
			SyncPostProcessor();
		}
		catch
		{
			// ignored
		}
	}

	// FXAA on parks built-in MSAA at 0 (scene bypasses it while post is on);
	// FXAA off restores the stashed level — unless the AA control was touched
	// manually while parked, which counts as the user's decision.
	private void checkBoxPostFxaa_CheckedChanged(object sender, EventArgs e)
	{
		try
		{
			if (checkBoxPostFxaa != null && AntialiasingLevel != null)
			{
				if (checkBoxPostFxaa.Checked)
				{
					if (Interface.CurrentOptions.AntiAliasingLevel != 0)
					{
						aaLevelBeforeFxaa = Interface.CurrentOptions.AntiAliasingLevel;
						Interface.CurrentOptions.AntiAliasingLevel = 0;
						AntialiasingLevel.Value = AntialiasingLevel.Minimum;
					}
				}
				else if (aaLevelBeforeFxaa.HasValue)
				{
					if (AntialiasingLevel.Value == AntialiasingLevel.Minimum)
					{
						Interface.CurrentOptions.AntiAliasingLevel = aaLevelBeforeFxaa.Value;
						AntialiasingLevel.Value = Math.Max(AntialiasingLevel.Minimum, Math.Min((decimal)aaLevelBeforeFxaa.Value, AntialiasingLevel.Maximum));
					}
					aaLevelBeforeFxaa = null;
				}
			}
			PostValueChanged(sender, e);
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
		// Keep the nested-tab numerics in sync (fires ValueChanged -> harmless re-sync).
		if (numericSaoSamples != null) numericSaoSamples.Value = AoLimits.Clamp(saoSamples, (int)numericSaoSamples.Minimum, (int)numericSaoSamples.Maximum);
		if (numericGtaoSlices != null) numericGtaoSlices.Value = AoLimits.Clamp(gtaoSlices, (int)numericGtaoSlices.Minimum, (int)numericGtaoSlices.Maximum);
		if (numericGtaoSteps != null) numericGtaoSteps.Value = AoLimits.Clamp(gtaoSteps, (int)numericGtaoSteps.Minimum, (int)numericGtaoSteps.Maximum);
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
            FormOptions optionsDialog = new FormOptions();
            DialogResult dialogResult = optionsDialog.ShowDialog();
            return dialogResult;
        }

        private void formOptions_Shown(object sender, EventArgs e)
        {
            button1.Focus();
        }

	    private readonly int previousAntialiasingLevel = Interface.CurrentOptions.AntiAliasingLevel;
	    private readonly int previousAnisotropicLevel = Interface.CurrentOptions.AnisotropicFilteringLevel;
	    private readonly int previousViewingDistance = Interface.CurrentOptions.ViewingDistance;
	    private readonly double previousNearClipBase = Interface.CurrentOptions.NearClipBase;
	    private bool GraphicsModeChanged = false;

        private void button1_Click(object sender, EventArgs e)
        {
            ShadowMapResolution previousShadowResolution = Interface.CurrentOptions.ShadowResolution;
	        ShadowDistance previousShadowDistance = Interface.CurrentOptions.ShadowDrawDistance;
	        ShadowCascadeCount previousShadowCascades = Interface.CurrentOptions.ShadowCascades;
	        double previousShadowStrength = Interface.CurrentOptions.ShadowStrength;
	        double previousShadowBias = Interface.CurrentOptions.ShadowBias;
	        double previousShadowNormalBias = Interface.CurrentOptions.ShadowNormalBias;
	        bool previousShadowFilterCascades = Interface.CurrentOptions.ShadowFilterCascades;

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
            Interface.CurrentOptions.AntiAliasingLevel = (int)AntialiasingLevel.Value;
            if (Interface.CurrentOptions.AntiAliasingLevel != previousAntialiasingLevel)
            {
                Program.Renderer.GraphicsMode = new GraphicsMode(new ColorFormat(8, 8, 8, 8), 24, 8, Interface.CurrentOptions.AntiAliasingLevel);
                // Parking-only change (FXAA auto-zero, control untouched since):
                // the mode switch cannot apply in-session anyway, so skip the
                // pointless route reload; the parked value is saved to cfg.
                bool parkedOnly = aaLevelBeforeFxaa.HasValue && AntialiasingLevel.Value == AntialiasingLevel.Minimum;
                if (!parkedOnly)
                {
                    GraphicsModeChanged = true;
                }
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
			Interface.CurrentOptions.LoadingLogo = checkBoxLogo.Checked;
			Interface.CurrentOptions.LoadingBackground = checkBoxBackgrounds.Checked;
			Interface.CurrentOptions.LoadingProgressBar = checkBoxProgressBar.Checked;
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
			Interface.CurrentOptions.ViewingDistance = (int)numericUpDownViewingDistance.Value;
			Interface.CurrentOptions.ObjectOptimizationMode = (ObjectOptimizationMode)comboBoxOptimizeObjects.SelectedIndex;
			Interface.CurrentOptions.NearClipBase = (double)numericUpDownNearClip.Value;
			// ensure viewing distance is greater than the near clipping plane to avoid rendering issues
			if (Interface.CurrentOptions.ViewingDistance <= Interface.CurrentOptions.NearClipBase)

			{
				Interface.CurrentOptions.ViewingDistance = (int)Math.Ceiling(Interface.CurrentOptions.NearClipBase) + 1;
			}
			Interface.CurrentOptions.QuadTreeLeafSize = Math.Max(50, (int)Math.Ceiling(Interface.CurrentOptions.ViewingDistance / 10.0d) * 10); // quad tree size set to 10% of viewing distance to the nearest 10

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

			// Viewer post save (tiers + nested-tab rows live in CurrentOptions already)
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
				if (numericAoPower != null)
				{
					Interface.CurrentOptions.AoPower = (float)AoLimits.Clamp(numericAoPower.Value, 0.5m, 3);
				}
				if (comboAoResolution != null && comboAoResolution.SelectedIndex >= 0)
				{
					Interface.CurrentOptions.AoResolutionScale = comboAoResolution.SelectedIndex == 0 ? 0.25f : comboAoResolution.SelectedIndex == 2 ? 1.0f : 0.5f;
				}
				if (numericAoBlurRadius != null)
				{
					Interface.CurrentOptions.AoBlurRadius = (int)AoLimits.Clamp(numericAoBlurRadius.Value, 0, 8);
				}
				if (checkBoxAoDebugView != null)
				{
					Interface.CurrentOptions.AoDebugView = checkBoxAoDebugView.Checked ? 1 : 0;
				}
				if (numericSaoSamples != null)
				{
					Interface.CurrentOptions.SaoSamples = (int)AoLimits.Clamp(numericSaoSamples.Value, 1, 32);
				}
				if (numericAoHorizon != null)
				{
					Interface.CurrentOptions.AoHorizonThreshold = (float)AoLimits.Clamp(numericAoHorizon.Value, 0, 0.2m);
				}
				if (numericAoDetail != null)
				{
					Interface.CurrentOptions.AoDetailStrength = (float)AoLimits.Clamp(numericAoDetail.Value, 0, 5);
				}
				if (numericGtaoSlices != null)
				{
					Interface.CurrentOptions.GtaoSlices = (int)AoLimits.Clamp(numericGtaoSlices.Value, 1, 8);
				}
				if (numericGtaoSteps != null)
				{
					Interface.CurrentOptions.GtaoSteps = (int)AoLimits.Clamp(numericGtaoSteps.Value, 1, 8);
				}
				if (numericGtaoFalloff != null)
				{
					Interface.CurrentOptions.GtaoFalloffRange = (float)AoLimits.Clamp(numericGtaoFalloff.Value, 0.05m, 2);
				}
			}
			catch
			{
				// ignored
			}

			// VSync and FPS Limit
			Interface.CurrentOptions.VerticalSynchronization = comboBoxVSync.SelectedIndex == 1;
			// Map combo index to FPSLimit value
			int[] fpsPresets = { 0, 30, 60, 120, 240 };
			Interface.CurrentOptions.FPSLimit = comboBoxFPSLimit.SelectedIndex >= 0 ? fpsPresets[comboBoxFPSLimit.SelectedIndex] : 0;
			Program.Renderer.GameWindow.VSync = Interface.CurrentOptions.VerticalSynchronization ? OpenTK.VSyncMode.On : OpenTK.VSyncMode.Off;
			Program.Renderer.GameWindow.TargetRenderFrequency = Interface.CurrentOptions.FPSLimit > 0 ? Interface.CurrentOptions.FPSLimit : 0;

            // Sun direction is already updated in real-time via slider events


			Interface.CurrentOptions.Save(Path.CombineFile(Program.FileSystem.SettingsFolder, "1.5.0/options_rv.cfg"));
			for (int i = 0; i < Program.CurrentHost.Plugins.Length; i++)
			{
				if (Program.CurrentHost.Plugins[i].Object != null)
				{
					Program.CurrentHost.Plugins[i].Object.SetObjectParser(Interface.CurrentOptions.CurrentXParser);
					Program.CurrentHost.Plugins[i].Object.SetObjectParser(Interface.CurrentOptions.CurrentObjParser);
				}
			}
			//Check if interpolation mode or anisotropic filtering level has changed, and trigger a reload
			if (previousInterpolationMode != Interface.CurrentOptions.Interpolation || previousAnisotropicLevel != Interface.CurrentOptions.AnisotropicFilteringLevel || GraphicsModeChanged || Interface.CurrentOptions.ViewingDistance != previousViewingDistance ||
			    previousShadowResolution != Interface.CurrentOptions.ShadowResolution || previousShadowDistance != Interface.CurrentOptions.ShadowDrawDistance || previousShadowCascades != Interface.CurrentOptions.ShadowCascades ||
			    previousShadowStrength != Interface.CurrentOptions.ShadowStrength || previousShadowBias != Interface.CurrentOptions.ShadowBias || previousShadowNormalBias != Interface.CurrentOptions.ShadowNormalBias || 
			    Interface.CurrentOptions.NearClipBase != previousNearClipBase || previousShadowFilterCascades != Interface.CurrentOptions.ShadowFilterCascades)
			{
				DialogResult = DialogResult.OK;
			}
			else
			{
				DialogResult = DialogResult.Abort;
			}
			Close();

        }

	    protected override void OnClosing(CancelEventArgs cancelEventArgs)
	    {
			
	    }
    }
}
