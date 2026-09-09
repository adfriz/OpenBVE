using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Formats.OpenBve;
using ObjectViewer.Graphics;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.Input;
using Path = OpenBveApi.Path;

namespace ObjectViewer
{
	/// <summary>Holds the program specific options</summary>
	internal class Options : BaseOptions
	{
		private ObjectOptimizationMode objectOptimizationMode;

		internal int FPSLimit;

		internal string ObjectSearchDirectory;

		internal Key CameraMoveLeft;

		internal Key CameraMoveRight;

		internal Key CameraMoveUp;

		internal Key CameraMoveDown;

		internal Key CameraMoveForward;

		internal Key CameraMoveBackward;

		internal Color24 BackgroundColor;

		internal Color32 TextColor;

		/// <summary>
		/// The mode of optimization to be performed on an object
		/// </summary>
		internal ObjectOptimizationMode ObjectOptimizationMode
		{
			get => objectOptimizationMode;
			set
			{
				objectOptimizationMode = value;

				switch (value)
				{
					case ObjectOptimizationMode.None:
						ObjectOptimizationBasicThreshold = 0;
						break;
					case ObjectOptimizationMode.Low:
						ObjectOptimizationBasicThreshold = 1000;
						break;
					case ObjectOptimizationMode.High:
						ObjectOptimizationBasicThreshold = 10000;
						break;
				}
			}
		}

		internal Options()
		{
			VerticalSynchronization = true;
			FPSLimit = 0;
			ObjectOptimizationMode = ObjectOptimizationMode.Low;
			// GTAO default = Balanced tier (4 slices x 3 steps), matches BaseOptions + effect ctor.
			GtaoSlices = 4;
			GtaoSteps = 3;
			// Shadow settings use synced base defaults
		}

		public override void Save(string fileName)
		{
			try
			{
				CultureInfo Culture = CultureInfo.InvariantCulture;
				System.Text.StringBuilder Builder = new System.Text.StringBuilder();
				Builder.AppendLine("; Options");
				Builder.AppendLine("; =======");
				Builder.AppendLine("; This file was automatically generated. Please modify only if you know what you're doing.");
				Builder.AppendLine("; Object Viewer specific options file");
				Builder.AppendLine();
				Builder.AppendLine("[display]");
				Builder.AppendLine("vsync = " + (VerticalSynchronization ? "true" : "false"));
				Builder.AppendLine("fpslimit = " + FPSLimit.ToString(Culture));
				Builder.AppendLine("windowWidth = " + Program.Renderer.Screen.Width.ToString(Culture));
				Builder.AppendLine("windowHeight = " + Program.Renderer.Screen.Height.ToString(Culture));
				Builder.AppendLine("nearclipbase = " + NearClipBase.ToString(Culture));
				Builder.AppendLine("autoReloadObjects = " + (AutoReloadObjects ? "true" : "false"));
				Builder.AppendLine("backgroundColor = " + BackgroundColor);
				Builder.AppendLine("textColor = " + TextColor);
				Builder.AppendLine();
				Builder.AppendLine("[quality]");
				Builder.AppendLine("interpolation = " + Interpolation);
				Builder.AppendLine("anisotropicfilteringlevel = " + AnisotropicFilteringLevel.ToString(Culture));
				Builder.AppendLine("antialiasinglevel = " + AntiAliasingLevel.ToString(Culture));
				Builder.AppendLine("transparencyMode = " + ((int)TransparencyMode).ToString(Culture));
				Builder.AppendLine("shadowresolution = " + (int)ShadowResolution);
				Builder.AppendLine("shadowdrawdistance = " + ShadowDrawDistance);
				Builder.AppendLine("shadowcascades = " + (int)ShadowCascades);
				Builder.AppendLine("shadowstrength = " + ShadowStrength.ToString("0.00", Culture));
				Builder.AppendLine("shadowbias = " + ShadowBias.ToString("0.000000", Culture));
				Builder.AppendLine("shadownormalbias = " + ShadowNormalBias.ToString("0.00", Culture));
				Builder.AppendLine("lightazimuth = " + LightAzimuth.ToString(Culture));
				Builder.AppendLine("lightelevation = " + LightElevation.ToString(Culture));
				Builder.AppendLine();
				AppendPostProcessingSection(Builder);
				Builder.AppendLine();
				AppendAmbientOcclusionSection(Builder, Culture);
				Builder.AppendLine();
				Builder.AppendLine("[Parsers]");
				Builder.AppendLine("xObject = " + CurrentXParser);
				Builder.AppendLine("objObject = " + CurrentObjParser);
				Builder.AppendLine();
				Builder.AppendLine("[objectOptimization]");
				Builder.AppendLine($"mode = {ObjectOptimizationMode}");
				Builder.AppendLine();
				Builder.AppendLine("[Folders]");
				Builder.AppendLine($"objectsearch = {ObjectSearchDirectory}");
				Builder.AppendLine("[Keys]");
				Builder.AppendLine("left = " + CameraMoveLeft);
				Builder.AppendLine("right = " + CameraMoveRight);
				Builder.AppendLine("up = " + CameraMoveUp);
				Builder.AppendLine("down = " + CameraMoveDown);
				Builder.AppendLine("forward = " + CameraMoveForward);
				Builder.AppendLine("backward = " + CameraMoveBackward);
				File.WriteAllText(fileName, Builder.ToString(), new System.Text.UTF8Encoding(true));
			}
			catch
			{
				MessageBox.Show("An error occured whilst saving the options to disk." + Environment.NewLine +
								"Please ensure you have write permission.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private static Key ResetIfUnknown(Key key, Key defaultKey)
		{
			return key == Key.Unknown ? defaultKey : key;
		}

		internal static void LoadOptions()
		{
			Interface.CurrentOptions = new Options
			{
				ViewingDistance = 1000, // fixed
				CameraMoveLeft = Key.A,
				CameraMoveRight = Key.D,
				CameraMoveUp = Key.W,
				CameraMoveDown = Key.S,
				CameraMoveForward = Key.Q,
				CameraMoveBackward = Key.E
			};
			string optionsFolder = Path.CombineDirectory(Program.FileSystem.SettingsFolder, "1.5.0");
			if (!Directory.Exists(optionsFolder))
			{
				Directory.CreateDirectory(optionsFolder);
			}
			string configFile = Path.CombineFile(optionsFolder, "options_ov.cfg");
			if (!File.Exists(configFile))
			{
				//Attempt to load and upgrade a prior configuration file
				string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				configFile = Path.CombineFile(Path.CombineDirectory(Path.CombineDirectory(assemblyFolder, "UserData"), "Settings"), "options_ov.cfg");

				if (!File.Exists(configFile))
				{
					//If no object viewer specific configuration file exists, then try the main OpenBVE configuration file
					//Write out to a new viewer specific file though
					configFile = Path.CombineFile(Program.FileSystem.SettingsFolder, "1.5.0/options.cfg");
				}
			}

			if (File.Exists(configFile))
			{
				ConfigFile<OptionsSection, OptionsKey> cfg = new ConfigFile<OptionsSection, OptionsKey>(File.ReadAllLines(configFile, new System.Text.UTF8Encoding()), configFile, Program.CurrentHost);

				while (cfg.RemainingSubBlocks > 0)
				{
					Block<OptionsSection, OptionsKey> block = cfg.ReadNextBlock();
					switch (block.Key)
					{
						case OptionsSection.Display:
							block.TryGetValue(OptionsKey.WindowWidth, ref Interface.CurrentOptions.WindowWidth, NumberRange.Positive);
							block.TryGetValue(OptionsKey.WindowHeight, ref Interface.CurrentOptions.WindowHeight, NumberRange.Positive);
							block.TryGetValue(OptionsKey.NearClipBase, ref Interface.CurrentOptions.NearClipBase, NumberRange.Positive);
							block.GetValue(OptionsKey.VSync, out Interface.CurrentOptions.VerticalSynchronization);
							block.GetValue(OptionsKey.FPSLimit, out Interface.CurrentOptions.FPSLimit);
							if (Interface.CurrentOptions.FPSLimit < 0)
							{
								Interface.CurrentOptions.FPSLimit = 0;
							}
							// ensure viewing distance is greater than the near clipping plane to avoid rendering issues
							if (Interface.CurrentOptions.ViewingDistance <= Interface.CurrentOptions.NearClipBase)
							{
								Interface.CurrentOptions.ViewingDistance = (int)Math.Ceiling(Interface.CurrentOptions.NearClipBase) + 1;
							}

							block.GetValue(OptionsKey.AutoReloadObjects, out Interface.CurrentOptions.AutoReloadObjects);
							block.GetColor24(OptionsKey.BackgroundColor, out Interface.CurrentOptions.BackgroundColor);
							block.GetColor32(OptionsKey.TextColor, out Interface.CurrentOptions.TextColor);
							break;
						case OptionsSection.Quality:
							block.GetEnumValue(OptionsKey.Interpolation, out Interface.CurrentOptions.Interpolation);
							block.TryGetValue(OptionsKey.AnisotropicFilteringLevel, ref Interface.CurrentOptions.AnisotropicFilteringLevel);
							block.TryGetValue(OptionsKey.AntiAliasingLevel, ref Interface.CurrentOptions.AntiAliasingLevel);
							block.GetEnumValue(OptionsKey.TransparencyMode, out Interface.CurrentOptions.TransparencyMode);
							block.TryGetEnumValue(OptionsKey.ShadowResolution, ref Interface.CurrentOptions.ShadowResolution);
							block.TryGetEnumValue(OptionsKey.ShadowDrawDistance, ref Interface.CurrentOptions.ShadowDrawDistance);
							block.TryGetEnumValue(OptionsKey.ShadowCascades, ref Interface.CurrentOptions.ShadowCascades);
							block.TryGetValue(OptionsKey.ShadowStrength, ref Interface.CurrentOptions.ShadowStrength, NumberRange.Positive);
							block.TryGetValue(OptionsKey.ShadowBias, ref Interface.CurrentOptions.ShadowBias);
							block.TryGetValue(OptionsKey.ShadowNormalBias, ref Interface.CurrentOptions.ShadowNormalBias);
							block.TryGetValue(OptionsKey.LightAzimuth, ref Interface.CurrentOptions.LightAzimuth);
							block.TryGetValue(OptionsKey.LightElevation, ref Interface.CurrentOptions.LightElevation);
							break;
						case OptionsSection.Parsers:
							block.GetEnumValue(OptionsKey.XObject, out Interface.CurrentOptions.CurrentXParser);
							block.GetEnumValue(OptionsKey.ObjObject, out Interface.CurrentOptions.CurrentObjParser);
							block.GetValue(OptionsKey.GDIPlus, out Interface.CurrentOptions.UseGDIDecoders);
							break;
						case OptionsSection.ObjectOptimization:
							block.GetEnumValue(OptionsKey.Mode, out ObjectOptimizationMode mode);
							Interface.CurrentOptions.ObjectOptimizationMode = mode; // can't set an accessor value directly
							break;
						case OptionsSection.Folders:
							block.GetValue(OptionsKey.ObjectSearch, out string folder);
							if (Directory.Exists(folder))
							{
								Interface.CurrentOptions.ObjectSearchDirectory = folder;
							}
							break;
						case OptionsSection.Keys:
							block.GetEnumValue(OptionsKey.Left, out Interface.CurrentOptions.CameraMoveLeft);
							block.GetEnumValue(OptionsKey.Right, out Interface.CurrentOptions.CameraMoveRight);
							block.GetEnumValue(OptionsKey.Up, out Interface.CurrentOptions.CameraMoveUp);
							block.GetEnumValue(OptionsKey.Down, out Interface.CurrentOptions.CameraMoveDown);
							block.GetEnumValue(OptionsKey.Forward, out Interface.CurrentOptions.CameraMoveForward);
							block.GetEnumValue(OptionsKey.Backward, out Interface.CurrentOptions.CameraMoveBackward);
							// Reset any invalid or unknown camera keys back to their defaults
							Interface.CurrentOptions.CameraMoveLeft = ResetIfUnknown(Interface.CurrentOptions.CameraMoveLeft, Key.A);
							Interface.CurrentOptions.CameraMoveRight = ResetIfUnknown(Interface.CurrentOptions.CameraMoveRight, Key.D);
							Interface.CurrentOptions.CameraMoveUp = ResetIfUnknown(Interface.CurrentOptions.CameraMoveUp, Key.W);
							Interface.CurrentOptions.CameraMoveDown = ResetIfUnknown(Interface.CurrentOptions.CameraMoveDown, Key.S);
							Interface.CurrentOptions.CameraMoveForward = ResetIfUnknown(Interface.CurrentOptions.CameraMoveForward, Key.Q);
							Interface.CurrentOptions.CameraMoveBackward = ResetIfUnknown(Interface.CurrentOptions.CameraMoveBackward, Key.E);
							break;
						case OptionsSection.PostProcessing:
						{
							block.TryGetValue(OptionsKey.EnablePostProcessing, ref Interface.CurrentOptions.EnablePostProcessing);
							if (block.TryGetValue(OptionsKey.PostEffectOrder, ref Interface.CurrentOptions.PostEffectOrder))
							{
								Interface.CurrentOptions.PostEffectOrder = BaseOptions.NormalizePostEffectOrder(Interface.CurrentOptions.PostEffectOrder);
							}
							block.TryGetValue(OptionsKey.PostFxaa, ref Interface.CurrentOptions.PostFxaa);
							block.TryGetValue(OptionsKey.PostSharpen, ref Interface.CurrentOptions.PostSharpen);
							block.TryGetValue(OptionsKey.PostVignette, ref Interface.CurrentOptions.PostVignette);
							break;
						}
						case OptionsSection.AmbientOcclusion:
						{
							block.TryGetEnumValue(OptionsKey.AoMode, ref Interface.CurrentOptions.AoMode);
							double dTmp;
							dTmp = Interface.CurrentOptions.AoRadius;
							if (block.TryGetValue(OptionsKey.AoRadius, ref dTmp))
							{
								Interface.CurrentOptions.AoRadius = (float)AoLimits.Clamp(dTmp, 0.1, 5.0);
							}
							dTmp = Interface.CurrentOptions.AoIntensity;
							if (block.TryGetValue(OptionsKey.AoIntensity, ref dTmp))
							{
								Interface.CurrentOptions.AoIntensity = (float)AoLimits.Clamp(dTmp, 0.0, 2.0);
							}
							dTmp = Interface.CurrentOptions.AoPower;
							if (block.TryGetValue(OptionsKey.AoPower, ref dTmp))
							{
								Interface.CurrentOptions.AoPower = (float)AoLimits.Clamp(dTmp, 0.5, 3.0);
							}
							dTmp = Interface.CurrentOptions.AoBias;
							if (block.TryGetValue(OptionsKey.AoBias, ref dTmp))
							{
								Interface.CurrentOptions.AoBias = (float)AoLimits.Clamp(dTmp, 0.0, 1.0);
							}
							dTmp = Interface.CurrentOptions.AoResolutionScale;
							if (block.TryGetValue(OptionsKey.AoResolutionScale, ref dTmp))
							{
								Interface.CurrentOptions.AoResolutionScale = (float)AoLimits.SnapScale(dTmp);
							}
							int iTmp;
							iTmp = Interface.CurrentOptions.AoBlurRadius;
							if (block.TryGetValue(OptionsKey.AoBlurRadius, ref iTmp))
							{
								Interface.CurrentOptions.AoBlurRadius = AoLimits.Clamp(iTmp, 0, 8);
							}
							dTmp = Interface.CurrentOptions.AoBlurSharpness;
							if (block.TryGetValue(OptionsKey.AoBlurSharpness, ref dTmp))
							{
								Interface.CurrentOptions.AoBlurSharpness = (float)AoLimits.Clamp(dTmp, 0.0, 1.0);
							}
							block.TryGetValue(OptionsKey.AoAffectCab3D, ref Interface.CurrentOptions.AoAffectCab3D);
							iTmp = Interface.CurrentOptions.AoDebugView;
							if (block.TryGetValue(OptionsKey.AoDebugView, ref iTmp))
							{
								Interface.CurrentOptions.AoDebugView = AoLimits.Clamp(iTmp, 0, 1);
							}
							iTmp = Interface.CurrentOptions.SaoSamples;
							if (block.TryGetValue(OptionsKey.SaoSamples, ref iTmp))
							{
								Interface.CurrentOptions.SaoSamples = AoLimits.Clamp(iTmp, 1, 32);
							}
							iTmp = Interface.CurrentOptions.SaoSpiralTurns;
							if (block.TryGetValue(OptionsKey.SaoSpiralTurns, ref iTmp))
							{
								Interface.CurrentOptions.SaoSpiralTurns = AoLimits.Clamp(iTmp, 1, 16);
							}
							dTmp = Interface.CurrentOptions.AoHorizonThreshold;
							if (block.TryGetValue(OptionsKey.AoHorizonThreshold, ref dTmp))
							{
								Interface.CurrentOptions.AoHorizonThreshold = (float)AoLimits.Clamp(dTmp, 0.0, 0.2);
							}
							dTmp = Interface.CurrentOptions.AoDetailStrength;
							if (block.TryGetValue(OptionsKey.AoDetailStrength, ref dTmp))
							{
								Interface.CurrentOptions.AoDetailStrength = (float)AoLimits.Clamp(dTmp, 0.0, 5.0);
							}
							iTmp = Interface.CurrentOptions.GtaoSlices;
							if (block.TryGetValue(OptionsKey.GtaoSlices, ref iTmp))
							{
								Interface.CurrentOptions.GtaoSlices = AoLimits.Clamp(iTmp, 1, 8);
							}
							iTmp = Interface.CurrentOptions.GtaoSteps;
							if (block.TryGetValue(OptionsKey.GtaoSteps, ref iTmp))
							{
								Interface.CurrentOptions.GtaoSteps = AoLimits.Clamp(iTmp, 1, 8);
							}
							dTmp = Interface.CurrentOptions.GtaoFalloffRange;
							if (block.TryGetValue(OptionsKey.GtaoFalloffRange, ref dTmp))
							{
								Interface.CurrentOptions.GtaoFalloffRange = (float)AoLimits.Clamp(dTmp, 0.05, 2.0);
							}
							break;
						}

					}
				}
			}
		}
	}
}
