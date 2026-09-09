using System;
using System.Windows.Forms;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;

namespace OpenBve
{
	internal partial class formMain
	{
		// Post-processing + AO controls (created programmatically to keep Designer untouched).
		// Order via options.cfg PostEffectOrder CSV.
		private System.Windows.Forms.GroupBox groupboxPostProcessing;
		private System.Windows.Forms.CheckBox checkboxPostEnabled;
		private System.Windows.Forms.CheckBox checkboxPostFxaa;
		private System.Windows.Forms.CheckBox checkboxPostSharpen;
		private System.Windows.Forms.CheckBox checkboxPostVignette;
		private System.Windows.Forms.Label labelPostOrderInfo;

		private System.Windows.Forms.GroupBox groupboxAO;
		private System.Windows.Forms.Label labelAoMode;
		private System.Windows.Forms.ComboBox comboboxAoMode;
		private System.Windows.Forms.Label labelAoRadius;
		private System.Windows.Forms.NumericUpDown updownAoRadius;
		private System.Windows.Forms.Label labelAoIntensity;
		private System.Windows.Forms.NumericUpDown updownAoIntensity;
		private System.Windows.Forms.Label labelAoPower;
		private System.Windows.Forms.NumericUpDown updownAoPower;
		private System.Windows.Forms.Label labelAoBias;
		private System.Windows.Forms.NumericUpDown updownAoBias;
		// Only the two most relevant extra fields get UI; SaoSamples / SaoSpiralTurns /
		// AoBlurRadius stay cfg-only (set via presets or options.cfg, clamped on load).
		private System.Windows.Forms.Label labelGtaoFalloff;
		private System.Windows.Forms.NumericUpDown updownGtaoFalloff;
		private System.Windows.Forms.Label labelAoBlurSharpness;
		private System.Windows.Forms.NumericUpDown updownAoBlurSharpness;
		private System.Windows.Forms.Label labelAoResolution;
		private System.Windows.Forms.ComboBox comboboxAoResolution;
		private System.Windows.Forms.CheckBox checkboxAoAffectCab3D;
		private System.Windows.Forms.CheckBox checkboxAoDebugView;
		private System.Windows.Forms.Button buttonAoPresetLow;
		private System.Windows.Forms.Button buttonAoPresetBalanced;
		private System.Windows.Forms.Button buttonAoPresetQuality;

		private bool postUiInitialized;

		/// <summary>Whether compute AO can be offered (renderer capability, false when renderer is null).</summary>
		private bool PostSupportsCompute
		{
			get { return LibRender2.Menu.MenuBuilder.SupportsCompute(Program.Renderer); }
		}

		private void InitializePostProcessingUI()
		{
			if (postUiInitialized)
			{
				return;
			}
			postUiInitialized = true;
			try
			{
				panelOptionsPage2.AutoScroll = true;

				groupboxPostProcessing = new System.Windows.Forms.GroupBox
				{
					Location = new System.Drawing.Point(0, 570),
					Name = "groupboxPostProcessing",
					Size = new System.Drawing.Size(321, 165),
					TabIndex = 40,
					TabStop = false,
					Text = "Post-Processing"
				};
				checkboxPostEnabled = new System.Windows.Forms.CheckBox
				{
					AutoSize = true,
					Location = new System.Drawing.Point(8, 20),
					Name = "checkboxPostEnabled",
					Size = new System.Drawing.Size(150, 17),
					Text = "Enable post-processing",
					UseVisualStyleBackColor = true
				};
				checkboxPostEnabled.CheckedChanged += checkboxPostEnabled_CheckedChanged;
				checkboxPostFxaa = new System.Windows.Forms.CheckBox
				{
					AutoSize = true, Location = new System.Drawing.Point(8, 43), Name = "checkboxPostFxaa", Text = "FXAA", UseVisualStyleBackColor = true
				};
				checkboxPostFxaa.CheckedChanged += PostEffectCheckboxChanged;
				checkboxPostSharpen = new System.Windows.Forms.CheckBox
				{
					AutoSize = true, Location = new System.Drawing.Point(8, 63), Name = "checkboxPostSharpen", Text = "Sharpen", UseVisualStyleBackColor = true
				};
				checkboxPostSharpen.CheckedChanged += PostEffectCheckboxChanged;
				checkboxPostVignette = new System.Windows.Forms.CheckBox
				{
					AutoSize = true, Location = new System.Drawing.Point(120, 43), Name = "checkboxPostVignette", Text = "Vignette", UseVisualStyleBackColor = true
				};
				checkboxPostVignette.CheckedChanged += PostEffectCheckboxChanged;
				labelPostOrderInfo = new System.Windows.Forms.Label
				{
					Location = new System.Drawing.Point(8, 108),
					Name = "labelPostOrderInfo",
					Size = new System.Drawing.Size(305, 50),
					Text = "Order via options.cfg PostEffectOrder CSV."
				};
				groupboxPostProcessing.Controls.Add(checkboxPostEnabled);
				groupboxPostProcessing.Controls.Add(checkboxPostFxaa);
				groupboxPostProcessing.Controls.Add(checkboxPostSharpen);
				groupboxPostProcessing.Controls.Add(checkboxPostVignette);
				groupboxPostProcessing.Controls.Add(labelPostOrderInfo);
				panelOptionsPage2.Controls.Add(groupboxPostProcessing);

				groupboxAO = new System.Windows.Forms.GroupBox
				{
					Location = new System.Drawing.Point(330, 570),
					Name = "groupboxAO",
					Size = new System.Drawing.Size(321, 350),
					TabIndex = 41,
					TabStop = false,
					Text = "Ambient Occlusion"
				};
				labelAoMode = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 22), Name = "labelAoMode", Text = "Mode:" };
				comboboxAoMode = new System.Windows.Forms.ComboBox
				{
					DropDownStyle = ComboBoxStyle.DropDownList,
					FormattingEnabled = true,
					Location = new System.Drawing.Point(120, 19),
					Name = "comboboxAoMode",
					Size = new System.Drawing.Size(168, 21)
				};
				comboboxAoMode.Items.AddRange(new object[] { "Off", "SAO", "GTAO" });
				comboboxAoMode.SelectedIndexChanged += comboboxAoMode_SelectedIndexChanged;

				labelAoRadius = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 50), Name = "labelAoRadius", Text = "Radius (m):" };
				updownAoRadius = new System.Windows.Forms.NumericUpDown
				{
					DecimalPlaces = 2, Increment = 0.05m, Location = new System.Drawing.Point(120, 48),
					Maximum = 5, Minimum = 0.1m, Name = "updownAoRadius", Size = new System.Drawing.Size(120, 20), Value = 0.8m
				};
				updownAoRadius.ValueChanged += AoNumericChanged;
				labelAoIntensity = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 74), Name = "labelAoIntensity", Text = "Intensity:" };
				updownAoIntensity = new System.Windows.Forms.NumericUpDown
				{
					DecimalPlaces = 2, Increment = 0.05m, Location = new System.Drawing.Point(120, 72),
					Maximum = 2, Minimum = 0, Name = "updownAoIntensity", Size = new System.Drawing.Size(120, 20), Value = 1.0m
				};
				updownAoIntensity.ValueChanged += AoNumericChanged;
				labelAoPower = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 98), Name = "labelAoPower", Text = "Power:" };
				updownAoPower = new System.Windows.Forms.NumericUpDown
				{
					DecimalPlaces = 2, Increment = 0.05m, Location = new System.Drawing.Point(120, 96),
					Maximum = 3, Minimum = 0.5m, Name = "updownAoPower", Size = new System.Drawing.Size(120, 20), Value = 2.0m
				};
				updownAoPower.ValueChanged += AoNumericChanged;
				labelAoBias = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 122), Name = "labelAoBias", Text = "Bias:" };
				updownAoBias = new System.Windows.Forms.NumericUpDown
				{
					DecimalPlaces = 3, Increment = 0.005m, Location = new System.Drawing.Point(120, 120),
					Maximum = 1, Minimum = 0, Name = "updownAoBias", Size = new System.Drawing.Size(120, 20), Value = 0.05m
				};
			updownAoBias.ValueChanged += AoNumericChanged;
			labelGtaoFalloff = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 146), Name = "labelGtaoFalloff", Text = "Falloff range:" };
			updownGtaoFalloff = new System.Windows.Forms.NumericUpDown
			{
				DecimalPlaces = 2, Increment = 0.05m, Location = new System.Drawing.Point(120, 144),
				Maximum = 2, Minimum = 0.05m, Name = "updownGtaoFalloff", Size = new System.Drawing.Size(120, 20), Value = 0.4m
			};
			updownGtaoFalloff.ValueChanged += AoNumericChanged;
			labelAoBlurSharpness = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 170), Name = "labelAoBlurSharpness", Text = "Blur sharpness:" };
			updownAoBlurSharpness = new System.Windows.Forms.NumericUpDown
			{
				DecimalPlaces = 3, Increment = 0.005m, Location = new System.Drawing.Point(120, 168),
				Maximum = 1, Minimum = 0, Name = "updownAoBlurSharpness", Size = new System.Drawing.Size(120, 20), Value = 0.01m
			};
			updownAoBlurSharpness.ValueChanged += AoNumericChanged;
			labelAoResolution = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(8, 196), Name = "labelAoResolution", Text = "Resolution:" };
			comboboxAoResolution = new System.Windows.Forms.ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				FormattingEnabled = true,
				Location = new System.Drawing.Point(120, 193),
				Name = "comboboxAoResolution",
				Size = new System.Drawing.Size(168, 21)
			};
			comboboxAoResolution.Items.AddRange(new object[] { "0.25", "0.5", "1.0" });
			comboboxAoResolution.SelectedIndexChanged += AoNumericChanged;
			checkboxAoAffectCab3D = new System.Windows.Forms.CheckBox
			{
				AutoSize = true, Location = new System.Drawing.Point(8, 221), Name = "checkboxAoAffectCab3D", Text = "Affect 3D cab", UseVisualStyleBackColor = true
			};
			checkboxAoAffectCab3D.CheckedChanged += AoNumericChanged;
			checkboxAoDebugView = new System.Windows.Forms.CheckBox
			{
				AutoSize = true, Location = new System.Drawing.Point(8, 241), Name = "checkboxAoDebugView", Text = "AO-only debug view", UseVisualStyleBackColor = true
			};
			checkboxAoDebugView.CheckedChanged += AoNumericChanged;
			buttonAoPresetLow = new System.Windows.Forms.Button
			{
					Location = new System.Drawing.Point(8, 268), Name = "buttonAoPresetLow", Size = new System.Drawing.Size(95, 23), Text = "Fast", UseVisualStyleBackColor = true
			};
			buttonAoPresetLow.Click += buttonAoPresetLow_Click;
			buttonAoPresetBalanced = new System.Windows.Forms.Button
			{
				Location = new System.Drawing.Point(110, 268), Name = "buttonAoPresetBalanced", Size = new System.Drawing.Size(95, 23), Text = "Balanced", UseVisualStyleBackColor = true
			};
			buttonAoPresetBalanced.Click += buttonAoPresetBalanced_Click;
			buttonAoPresetQuality = new System.Windows.Forms.Button
			{
				Location = new System.Drawing.Point(212, 268), Name = "buttonAoPresetQuality", Size = new System.Drawing.Size(95, 23), Text = "Quality", UseVisualStyleBackColor = true
			};
			buttonAoPresetQuality.Click += buttonAoPresetQuality_Click;
			System.Windows.Forms.Label labelAoPresetInfo = new System.Windows.Forms.Label
			{
				Location = new System.Drawing.Point(8, 296),
				Name = "labelAoPresetInfo",
				Size = new System.Drawing.Size(305, 45),
				Text = "Preset sets SaoSamples / GtaoSlices x Steps."
			};
				groupboxAO.Controls.Add(labelAoMode);
				groupboxAO.Controls.Add(comboboxAoMode);
				groupboxAO.Controls.Add(labelAoRadius);
				groupboxAO.Controls.Add(updownAoRadius);
				groupboxAO.Controls.Add(labelAoIntensity);
				groupboxAO.Controls.Add(updownAoIntensity);
				groupboxAO.Controls.Add(labelAoPower);
				groupboxAO.Controls.Add(updownAoPower);
			groupboxAO.Controls.Add(labelAoBias);
			groupboxAO.Controls.Add(updownAoBias);
			groupboxAO.Controls.Add(labelGtaoFalloff);
			groupboxAO.Controls.Add(updownGtaoFalloff);
			groupboxAO.Controls.Add(labelAoBlurSharpness);
			groupboxAO.Controls.Add(updownAoBlurSharpness);
				groupboxAO.Controls.Add(labelAoResolution);
				groupboxAO.Controls.Add(comboboxAoResolution);
				groupboxAO.Controls.Add(checkboxAoAffectCab3D);
				groupboxAO.Controls.Add(checkboxAoDebugView);
				groupboxAO.Controls.Add(buttonAoPresetLow);
				groupboxAO.Controls.Add(buttonAoPresetBalanced);
				groupboxAO.Controls.Add(buttonAoPresetQuality);
				groupboxAO.Controls.Add(labelAoPresetInfo);
				panelOptionsPage2.Controls.Add(groupboxAO);

				LoadPostProcessingUI();
				UpdatePostControlsEnabled();
				ApplyPostLanguage();
			}
			catch
			{
				// ignored: post UI must never break the main form
			}
		}

		private void LoadPostProcessingUI()
		{
			try
			{
				if (checkboxPostEnabled == null || comboboxAoMode == null)
				{
					return;
				}
				checkboxPostEnabled.Checked = Interface.CurrentOptions.EnablePostProcessing;
				checkboxPostFxaa.Checked = Interface.CurrentOptions.PostFxaa;
				checkboxPostSharpen.Checked = Interface.CurrentOptions.PostSharpen;
				checkboxPostVignette.Checked = Interface.CurrentOptions.PostVignette;
				comboboxAoMode.SelectedIndex = AoModeMapper.ToSelectedIndex(Interface.CurrentOptions.AoMode);
				updownAoRadius.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoRadius, updownAoRadius.Minimum, updownAoRadius.Maximum);
				updownAoIntensity.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoIntensity, updownAoIntensity.Minimum, updownAoIntensity.Maximum);
				updownAoPower.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoPower, updownAoPower.Minimum, updownAoPower.Maximum);
			updownAoBias.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoBias, updownAoBias.Minimum, updownAoBias.Maximum);
			updownGtaoFalloff.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.GtaoFalloffRange, updownGtaoFalloff.Minimum, updownGtaoFalloff.Maximum);
			updownAoBlurSharpness.Value = AoLimits.Clamp((decimal)Interface.CurrentOptions.AoBlurSharpness, updownAoBlurSharpness.Minimum, updownAoBlurSharpness.Maximum);
			float rs = Interface.CurrentOptions.AoResolutionScale;
			float snapped = AoLimits.SnapScale(rs);
			comboboxAoResolution.SelectedIndex = snapped <= 0.25f ? 0 : (snapped >= 1.0f ? 2 : 1);
				checkboxAoAffectCab3D.Checked = Interface.CurrentOptions.AoAffectCab3D;
				checkboxAoDebugView.Checked = Interface.CurrentOptions.AoDebugView != 0;
			}
			catch
			{
				// ignored
			}
		}

		private void SavePostProcessingUI()
		{
			try
			{
				if (checkboxPostEnabled == null || comboboxAoMode == null)
				{
					return;
				}
				Interface.CurrentOptions.EnablePostProcessing = checkboxPostEnabled.Checked;
				Interface.CurrentOptions.PostFxaa = checkboxPostFxaa.Checked;
				Interface.CurrentOptions.PostSharpen = checkboxPostSharpen.Checked;
				Interface.CurrentOptions.PostVignette = checkboxPostVignette.Checked;
				// PostEffectOrder stays via CSV; keep existing value.
				Interface.CurrentOptions.AoMode = AoModeMapper.FromSelectedIndex(comboboxAoMode.SelectedIndex);
				Interface.CurrentOptions.AoRadius = (float)updownAoRadius.Value;
				Interface.CurrentOptions.AoIntensity = (float)updownAoIntensity.Value;
				Interface.CurrentOptions.AoPower = (float)updownAoPower.Value;
				Interface.CurrentOptions.AoBias = (float)updownAoBias.Value;
			Interface.CurrentOptions.GtaoFalloffRange = (float)updownGtaoFalloff.Value;
			Interface.CurrentOptions.AoBlurSharpness = (float)updownAoBlurSharpness.Value;
				switch (comboboxAoResolution.SelectedIndex)
				{
					case 0: Interface.CurrentOptions.AoResolutionScale = 0.25f; break;
					case 2: Interface.CurrentOptions.AoResolutionScale = 1.0f; break;
					default: Interface.CurrentOptions.AoResolutionScale = 0.5f; break;
				}
			Interface.CurrentOptions.AoAffectCab3D = checkboxAoAffectCab3D.Checked;
			Interface.CurrentOptions.AoDebugView = checkboxAoDebugView.Checked ? 1 : 0;
			// SaoSamples / SaoSpiralTurns / AoBlurRadius are set via presets below (clamped on load);
			// SaoSpiralTurns / AoBlurRadius have no UI and stay editable via options.cfg.
			}
			catch
			{
				// ignored
			}
		}

		private void UpdatePostControlsEnabled()
		{
			try
			{
				if (groupboxAO == null || checkboxPostEnabled == null || comboboxAoMode == null)
				{
					return;
				}
				bool supportsCompute = PostSupportsCompute;
				// Mirror shadowEnabled pattern: grey the whole AO group when compute is unavailable.
				groupboxAO.Enabled = supportsCompute;
				if (!supportsCompute)
				{
				toolTip.SetToolTip(groupboxAO, "Requires OpenGL 4.3");
				toolTip.SetToolTip(comboboxAoMode, "Requires OpenGL 4.3");
					return;
				}
			bool master = checkboxPostEnabled.Checked;
			bool aoOn = AoModeMapper.FromSelectedIndex(comboboxAoMode.SelectedIndex) != AmbientOcclusionMode.Off;
			bool subEnabled = master && aoOn;
			updownAoRadius.Enabled = subEnabled;
			updownAoIntensity.Enabled = subEnabled;
			updownAoPower.Enabled = subEnabled;
			updownAoBias.Enabled = subEnabled;
			updownGtaoFalloff.Enabled = subEnabled;
			updownAoBlurSharpness.Enabled = subEnabled;
			comboboxAoResolution.Enabled = subEnabled;
			checkboxAoAffectCab3D.Enabled = subEnabled;
			checkboxAoDebugView.Enabled = subEnabled;
			buttonAoPresetLow.Enabled = subEnabled;
			buttonAoPresetBalanced.Enabled = subEnabled;
			buttonAoPresetQuality.Enabled = subEnabled;
			labelAoRadius.Enabled = subEnabled;
			labelAoIntensity.Enabled = subEnabled;
			labelAoPower.Enabled = subEnabled;
			labelAoBias.Enabled = subEnabled;
			labelGtaoFalloff.Enabled = subEnabled;
			labelAoBlurSharpness.Enabled = subEnabled;
				labelAoResolution.Enabled = subEnabled;
				labelAoMode.Enabled = master;
				// Keep groupbox itself enabled so master can always be toggled.
				groupboxPostProcessing.Enabled = true;
			}
			catch
			{
				// ignored
			}
		}

		private void ApplyPostLanguage()
		{
			try
			{
				if (groupboxPostProcessing == null || groupboxAO == null)
				{
					return;
				}
				groupboxPostProcessing.Text = GetPostString("postprocessing_header", "Post-Processing");
				checkboxPostEnabled.Text = GetPostString("postprocessing_enabled", "Enable post-processing");
				checkboxPostFxaa.Text = GetPostString("postprocessing_fxaa", "FXAA");
				checkboxPostSharpen.Text = GetPostString("postprocessing_sharpen", "Sharpen");
				checkboxPostVignette.Text = GetPostString("postprocessing_vignette", "Vignette");
				labelPostOrderInfo.Text = GetPostString("postprocessing_order_info", "Order via options.cfg PostEffectOrder CSV.");
				groupboxAO.Text = GetPostString("ao_header", "Ambient Occlusion");
				labelAoMode.Text = GetPostString("ao_mode", "Mode:");
				labelAoRadius.Text = GetPostString("ao_radius", "Radius (m):");
				labelAoIntensity.Text = GetPostString("ao_intensity", "Intensity:");
				labelAoPower.Text = GetPostString("ao_power", "Power:");
				labelAoBias.Text = GetPostString("ao_bias", "Bias:");
			labelGtaoFalloff.Text = GetPostString("ao_falloff", "Falloff range:");
			labelAoBlurSharpness.Text = GetPostString("ao_blursharpness", "Blur sharpness:");
				labelAoResolution.Text = GetPostString("ao_resolution", "Resolution:");
				checkboxAoAffectCab3D.Text = GetPostString("ao_affectcab3d", "Affect 3D cab");
				checkboxAoDebugView.Text = GetPostString("ao_debugview", "AO-only debug view");
				buttonAoPresetLow.Text = GetPostString("ao_preset_low", "Fast");
				buttonAoPresetBalanced.Text = GetPostString("ao_preset_balanced", "Balanced");
				buttonAoPresetQuality.Text = GetPostString("ao_preset_quality", "Quality");
			}
			catch
			{
				// ignored
			}
		}

		private static string GetPostString(string key, string fallback)
		{
			try
			{
				string s = Translations.GetInterfaceString(HostApplication.OpenBve, new[] { "options", key });
				if (string.IsNullOrWhiteSpace(s) || s == key)
				{
					return fallback;
				}
				return s;
			}
			catch
			{
				return fallback;
			}
		}

		private void ApplyAoPreset(int saoSamples, int gtaoSlices, int gtaoSteps)
		{
			try
			{
				Interface.CurrentOptions.SaoSamples = saoSamples;
				Interface.CurrentOptions.GtaoSlices = gtaoSlices;
				Interface.CurrentOptions.GtaoSteps = gtaoSteps;
				UpdatePostControlsEnabled();
				SyncPostChain();
			}
			catch
			{
				// ignored
			}
		}

		private void SyncPostChain()
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
				// ignored: preset sync must never break the form
			}
		}
	}
}
