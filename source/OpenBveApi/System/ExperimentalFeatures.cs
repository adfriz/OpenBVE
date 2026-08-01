using System;
using System.IO;
using System.Windows.Forms;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using Path = OpenBveApi.Path;

namespace OpenBveApi
{
	/// <summary>Represents a single experimental feature which can be enabled or disabled by the user</summary>
	public sealed class ExperimentalFeature
	{
		/// <summary>The key of the feature, used as the key within the options file and to read the option from a config block</summary>
		public OptionsKey Key;
		/// <summary>The translation keys for the name of the feature</summary>
		public string[] NameTranslation;
		/// <summary>Gets the current value of the feature from the specified options</summary>
		public Func<BaseOptions, bool> Get;
		/// <summary>Sets the current value of the feature on the specified options</summary>
		public Action<BaseOptions, bool> Set;
	}

	/// <summary>Provides the registry of all experimental features known to this build of the program</summary>
	/// <remarks>
	/// <para>Experimental features are disabled by default, and can only be enabled through the WinForms options dialogs - never from the in-game menu:</para>
	/// <list type="bullet">
	/// <item><description>OpenBVE: the Options dialog, "Experimental features" group.</description></item>
	/// <item><description>ObjectViewer and RouteViewer: the "Experimental" tab of the F8 options window.</description></item>
	/// </list>
	/// <para>If the program crashes whilst any experimental features are enabled, a crash marker file is written
	/// (see <see cref="WriteCrashMarker"/>). On the next start, every experimental feature is reset to its disabled
	/// default (see <see cref="CheckCrashMarkerAndReset"/>).</para>
	/// <para>To add a new experimental feature:</para>
	/// <list type="number">
	/// <item><description>Add a bool field (e.g. <c>EnableExampleFeature</c>) to <see cref="BaseOptions"/>.</description></item>
	/// <item><description>Add a matching member to the <c>OptionsKey</c> enum. The member name is used as the key
	/// within the options file, so pick a stable name from the start.</description></item>
	/// <item><description>Add an entry to <see cref="All"/>, wiring up <see cref="ExperimentalFeature.Key"/>, the
	/// <see cref="ExperimentalFeature.NameTranslation"/> key and the <see cref="ExperimentalFeature.Get"/> / <see cref="ExperimentalFeature.Set"/> delegates.</description></item>
	/// <item><description>Add the translation strings referenced by the entry to the <c>experimental</c> group of the language files.</description></item>
	/// </list>
	/// <para>The options dialogs and the options file are generated automatically from <see cref="All"/>;
	/// no other code changes are required.</para>
	/// <para>To graduate an experimental feature (move it to a stable, always-available option):</para>
	/// <list type="number">
	/// <item><description>Remove its entry from <see cref="All"/> - the checkbox and the options file key disappear automatically.</description></item>
	/// <item><description>Replace the field in <see cref="BaseOptions"/> with a regular option and the <c>OptionsKey</c> member
	/// with a normal key, then add save/load code to the relevant <c>Options</c> class (e.g. <c>OpenBve\System\Options.cs</c>).</description></item>
	/// <item><description>Optionally remove the obsolete <c>OptionsKey</c> member and the <c>OptionsSection.Experimental</c> enum value.
	/// Stale keys left behind by experimental versions of the option are ignored on load.</description></item>
	/// </list>
	/// </remarks>
	public static class ExperimentalFeatures
	{
		/// <summary>The name of the marker file which indicates that the program crashed whilst experimental features were enabled</summary>
		public const string CrashMarkerFileName = "experimental.crash";

		/// <summary>All experimental features known to this build of the program</summary>
		public static readonly ExperimentalFeature[] All =
		{
			new ExperimentalFeature
			{
				Key = OptionsKey.ExperimentalPlaceholder,
				NameTranslation = new[] {"experimental", "placeholder_name"},
				Get = options => options.EnableExperimentalPlaceholder,
				Set = (options, value) => options.EnableExperimentalPlaceholder = value
			}
		};

		/// <summary>Disables all experimental features on the specified options</summary>
		/// <param name="options">The options to reset</param>
		public static void ResetToSafe(BaseOptions options)
		{
			foreach (ExperimentalFeature feature in All)
			{
				feature.Set(options, false);
			}
		}

		/// <summary>Returns a comma-separated list of all currently enabled experimental features, or None if none are enabled</summary>
		/// <param name="options">The options to read the feature states from</param>
		public static string GetEnabledList(BaseOptions options)
		{
			string result = string.Empty;
			foreach (ExperimentalFeature feature in All)
			{
				if (feature.Get(options))
				{
					result += feature.Key.ToString() + ", ";
				}
			}
			return result.Length == 0 ? "None" : result.Substring(0, result.Length - 2);
		}

		/// <summary>Returns the full path of the crash marker file for the specified settings folder</summary>
		/// <param name="settingsFolder">The settings folder</param>
		private static string GetCrashMarkerFile(string settingsFolder)
		{
			return Path.CombineFile(Path.CombineDirectory(settingsFolder, "1.5.0"), CrashMarkerFileName);
		}

		/// <summary>Checks whether a crash marker file exists, and if so resets all experimental features and saves the options file. Never throws.</summary>
		/// <param name="settingsFolder">The settings folder</param>
		/// <param name="options">The options to reset</param>
		/// <param name="optionsFileToSave">The options file to save after a reset, or null to not save</param>
		/// <returns>True if a previous crash was detected and all experimental features have been reset</returns>
		public static bool CheckCrashMarkerAndReset(string settingsFolder, BaseOptions options, string optionsFileToSave)
		{
			try
			{
				string markerFile = GetCrashMarkerFile(settingsFolder);
				if (!File.Exists(markerFile))
				{
					return false;
				}
				// The program crashed the last time it was run with experimental features enabled,
				// so reset them all to their safe (disabled) defaults and save the options file
				ResetToSafe(options);
				if (optionsFileToSave != null)
				{
					options.Save(optionsFileToSave);
				}
				File.Delete(markerFile);
				return true;
			}
			catch
			{
				// If the reset or save fails, the marker file is left in place and the reset is retried on the next start
				return false;
			}
		}

		/// <summary>Creates a checkbox for the specified feature, bound to the specified options</summary>
		/// <param name="feature">The feature</param>
		/// <param name="options">The options the checkbox is bound to</param>
		public static System.Windows.Forms.CheckBox CreateCheckBox(ExperimentalFeature feature, BaseOptions options)
		{
			return new System.Windows.Forms.CheckBox
			{
				AutoSize = true,
				Text = Translations.GetInterfaceString(HostApplication.OpenBve, feature.NameTranslation),
				Tag = feature,
				Checked = feature.Get(options)
			};
		}

		/// <summary>Applies the state of all experimental feature checkboxes to the specified options</summary>
		/// <param name="controls">The control collection containing the checkboxes</param>
		/// <param name="options">The options to write the feature states to</param>
		public static void ApplyCheckBoxes(System.Windows.Forms.Control.ControlCollection controls, BaseOptions options)
		{
			foreach (System.Windows.Forms.Control control in controls)
			{
				if (control is System.Windows.Forms.CheckBox checkBox && checkBox.Tag is ExperimentalFeature feature)
				{
					feature.Set(options, checkBox.Checked);
				}
			}
		}

		/// <summary>Writes the crash marker file if any experimental features were enabled. Never throws.</summary>
		/// <param name="settingsFolder">The settings folder</param>
		/// <param name="options">The current options, or null if the options have not yet been loaded</param>
		public static void WriteCrashMarker(string settingsFolder, BaseOptions options)
		{
			try
			{
				if (options == null)
				{
					return;
				}
				string enabledList = GetEnabledList(options);
				if (enabledList == "None")
				{
					return;
				}
				string markerFile = GetCrashMarkerFile(settingsFolder);
				string directory = Path.GetDirectoryName(markerFile);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				File.WriteAllText(markerFile, "Experimental features enabled at time of crash: " + enabledList);
			}
			catch
			{
				// Ignored - this may be called from within a crash handler, and must never throw
			}
		}
	}
}
