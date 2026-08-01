using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using Path = OpenBveApi.Path;

namespace OpenBveApi
{
	/// <summary>The host applications in which an experimental feature can be enabled</summary>
	[Flags]
	public enum ExperimentalFeatureHost
	{
		/// <summary>The main game</summary>
		OpenBve = 1,
		/// <summary>Route Viewer</summary>
		RouteViewer = 2,
		/// <summary>Object Viewer</summary>
		ObjectViewer = 4
	}

	/// <summary>Marks a boolean field on <see cref="BaseOptions"/> as an experimental feature which can be enabled or disabled by the user</summary>
	/// <remarks>
	/// <para>The options key, translation ids and get/set delegates are all derived from the field automatically:</para>
	/// <list type="bullet">
	/// <item><description>The key within the options file defaults to the field name with any leading <c>Enable</c> removed,
	/// and must match a member of the <c>OptionsKey</c> enum. Override with <see cref="Key"/>.</description></item>
	/// <item><description>The name translation id defaults to the field name within the <c>experimental</c> group of the language files.
	/// Override with <see cref="NameId"/>.</description></item>
	/// <item><description>There is no description by default. Set <see cref="DescriptionId"/> to add one.</description></item>
	/// <item><description>The feature is available in all host applications by default. Override with <see cref="Hosts"/>
	/// to restrict it to specific applications (e.g. an Object Viewer only feature).</description></item>
	/// </list>
	/// </remarks>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ExperimentalFeatureAttribute : Attribute
	{
		/// <summary>The name of the matching <c>OptionsKey</c> enum member (used as the key within the options file), or null to derive it from the field name</summary>
		public string Key;
		/// <summary>The translation id of the feature name within the <c>experimental</c> group of the language files, or null to use the field name</summary>
		public string NameId;
		/// <summary>The translation id of the feature description within the <c>experimental</c> group of the language files, or null if the feature has no description</summary>
		public string DescriptionId;
		/// <summary>The host applications in which the feature is shown and persisted, or all applications by default</summary>
		public ExperimentalFeatureHost Hosts = ExperimentalFeatureHost.OpenBve | ExperimentalFeatureHost.RouteViewer | ExperimentalFeatureHost.ObjectViewer;
	}

	/// <summary>Represents a single experimental feature which can be enabled or disabled by the user</summary>
	public sealed class ExperimentalFeature
	{
		/// <summary>The key of the feature, used as the key within the options file and to read the option from a config block</summary>
		public OptionsKey Key;
		/// <summary>The translation keys for the name of the feature</summary>
		public string[] NameTranslation;
		/// <summary>The translation keys for the description of the feature, or null if the feature has no description</summary>
		public string[] DescriptionTranslation;
		/// <summary>The field name of the feature, used as a fallback name if the translation is missing</summary>
		public string FieldName;
		/// <summary>The host applications in which the feature is shown and persisted</summary>
		public ExperimentalFeatureHost Hosts;
		/// <summary>Gets the current value of the feature from the specified options</summary>
		public Func<BaseOptions, bool> Get;
		/// <summary>Sets the current value of the feature on the specified options</summary>
		public Action<BaseOptions, bool> Set;

		/// <summary>Returns the localised name of the feature, falling back to the field name if no translation exists</summary>
		/// <param name="host">The host application to read the translation for</param>
		public string GetName(HostApplication host)
		{
			string name = Translations.GetInterfaceString(host, NameTranslation);
			return string.IsNullOrEmpty(name) ? FieldName : name;
		}

		/// <summary>Returns the localised description of the feature, or null if the feature has no description or none is translated</summary>
		/// <param name="host">The host application to read the translation for</param>
		public string GetDescription(HostApplication host)
		{
			if (DescriptionTranslation == null)
			{
				return null;
			}
			string description = Translations.GetInterfaceString(host, DescriptionTranslation);
			return string.IsNullOrEmpty(description) ? null : description;
		}
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
	/// <item><description>Add a bool field marked with <c>[ExperimentalFeature]</c> (e.g. <c>[ExperimentalFeature] public bool EnableExampleFeature;</c>) to <see cref="BaseOptions"/>.</description></item>
	/// <item><description>Add a matching member to the <c>OptionsKey</c> enum. The member name is used as the key
	/// within the options file (derived from the field name unless overridden with <see cref="ExperimentalFeatureAttribute.Key"/>),
	/// so pick a stable name from the start.</description></item>
	/// <item><description>Add the translation strings to the <c>experimental</c> group of the language files. If a translation is
	/// missing, the field name is shown instead, so the feature remains visible.</description></item>
	/// </list>
	/// <para>The options dialogs and the options file are generated automatically from <see cref="All"/>;
	/// no other code changes are required.</para>
	/// <para>To graduate an experimental feature (move it to a stable, always-available option):</para>
	/// <list type="number">
	/// <item><description>Remove the <c>[ExperimentalFeature]</c> attribute from its field - the checkbox and the options file key disappear automatically.</description></item>
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

		/// <summary>All experimental features known to this build of the program, discovered from the fields of <see cref="BaseOptions"/> marked with <see cref="ExperimentalFeatureAttribute"/></summary>
		public static readonly ExperimentalFeature[] All;

		static ExperimentalFeatures()
		{
			All = DiscoverFeatures();
		}

		/// <summary>Builds the feature registry from every <see cref="ExperimentalFeatureAttribute"/>-annotated field on <see cref="BaseOptions"/></summary>
		private static ExperimentalFeature[] DiscoverFeatures()
		{
			List<ExperimentalFeature> features = new List<ExperimentalFeature>();
			foreach (FieldInfo field in typeof(BaseOptions).GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				ExperimentalFeatureAttribute attribute = (ExperimentalFeatureAttribute)Attribute.GetCustomAttribute(field, typeof(ExperimentalFeatureAttribute));
				if (attribute == null)
				{
					continue;
				}
				if (field.FieldType != typeof(bool))
				{
					throw new TypeLoadException("The experimental feature field '" + field.Name + "' in BaseOptions must be of type bool.");
				}
				string keyName = attribute.Key ?? (field.Name.StartsWith("Enable", StringComparison.Ordinal) ? field.Name.Substring("Enable".Length) : field.Name);
				if (!Enum.TryParse(keyName, true, out OptionsKey key))
				{
					throw new TypeLoadException("No OptionsKey member named '" + keyName + "' exists for the experimental feature field '" + field.Name + "'. Add it to the OptionsKey enum.");
				}
				features.Add(new ExperimentalFeature
				{
					Key = key,
					FieldName = field.Name,
					Hosts = attribute.Hosts,
					NameTranslation = new[] {"experimental", attribute.NameId ?? field.Name},
					DescriptionTranslation = attribute.DescriptionId == null ? null : new[] {"experimental", attribute.DescriptionId},
					Get = options => (bool)field.GetValue(options),
					Set = (options, value) => field.SetValue(options, value)
				});
			}
			return features.ToArray();
		}

		/// <summary>Returns the experimental features which are available in the specified host application</summary>
		/// <param name="host">The host application to filter for</param>
		public static ExperimentalFeature[] GetFeatures(ExperimentalFeatureHost host)
		{
			List<ExperimentalFeature> features = new List<ExperimentalFeature>();
			foreach (ExperimentalFeature feature in All)
			{
				if ((feature.Hosts & host) != 0)
				{
					features.Add(feature);
				}
			}
			return features.ToArray();
		}

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
				Text = feature.GetName(HostApplication.OpenBve),
				Tag = feature,
				Checked = feature.Get(options)
			};
		}

		/// <summary>Creates a feature list panel for the specified feature, containing a checkbox and (if defined) a description, bound to the specified options</summary>
		/// <param name="feature">The feature</param>
		/// <param name="options">The options the panel is bound to</param>
		/// <param name="width">The width of the panel in pixels</param>
		public static System.Windows.Forms.Control CreateFeaturePanel(ExperimentalFeature feature, BaseOptions options, int width)
		{
			System.Windows.Forms.CheckBox checkBox = new System.Windows.Forms.CheckBox
			{
				Text = feature.GetName(HostApplication.OpenBve),
				Tag = feature,
				Checked = feature.Get(options),
				Location = new System.Drawing.Point(0, 0),
				AutoSize = true
			};
			System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
			{
				Width = width,
				Height = checkBox.Height,
				Margin = new System.Windows.Forms.Padding(0, 4, 0, 4)
			};
			panel.Controls.Add(checkBox);
			string descriptionText = feature.GetDescription(HostApplication.OpenBve);
			if (descriptionText != null)
			{
				System.Windows.Forms.Label description = new System.Windows.Forms.Label
				{
					Text = descriptionText,
					Location = new System.Drawing.Point(24, checkBox.Height + 2),
					Width = width - 24,
					AutoSize = false,
					ForeColor = System.Drawing.SystemColors.GrayText,
					UseMnemonic = false
				};
				description.Height = System.Windows.Forms.TextRenderer.MeasureText(descriptionText, checkBox.Font, new System.Drawing.Size(description.Width, int.MaxValue), System.Windows.Forms.TextFormatFlags.WordBreak).Height + 2;
				panel.Height += description.Height + 2;
				panel.Controls.Add(description);
			}
			return panel;
		}

		/// <summary>Applies the state of all experimental feature checkboxes to the specified options</summary>
		/// <param name="controls">The control collection containing the checkboxes (searched recursively)</param>
		/// <param name="options">The options to write the feature states to</param>
		public static void ApplyCheckBoxes(System.Windows.Forms.Control.ControlCollection controls, BaseOptions options)
		{
			foreach (System.Windows.Forms.Control control in controls)
			{
				if (control is System.Windows.Forms.CheckBox checkBox && checkBox.Tag is ExperimentalFeature feature)
				{
					feature.Set(options, checkBox.Checked);
				}
				else if (control.HasChildren)
				{
					ApplyCheckBoxes(control.Controls, options);
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
