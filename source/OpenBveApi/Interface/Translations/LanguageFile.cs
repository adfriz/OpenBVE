using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenBveApi.Interface {
	public static partial class Translations {

		/// <summary>Loads all available language files from the specified folder</summary>
        public static void LoadLanguageFiles(string languageFolder) {
			if (AvailableNewLanguages.Count > 2)
			{
				// Don't re-load languages if already present, e.g. restart
				return;
			}
			if (!Directory.Exists(languageFolder))
			{
				Console.WriteLine(@"The default language files have been moved or deleted.");
				LoadEmbeddedLanguage();
				return;
			}
            try {
				string[] languageFiles = Directory.GetFiles(languageFolder, "*.xlf");
	            if (languageFiles.Length == 0)
	            {
		            Console.WriteLine(@"No valid language files were found.");
					LoadEmbeddedLanguage();
		            return;
	            }
                foreach (string language in languageFiles) {
	                try
	                {
		                using (FileStream stream = new FileStream(language, FileMode.Open, FileAccess.Read))
		                {
			                AvailableNewLanguages.Add(System.IO.Path.GetFileNameWithoutExtension(language), new NewLanguage(stream, language));
		                }
	                }
	                catch
	                {
						//Corrupt language file? Just ignore
	                }
                }
            } catch {
                Console.WriteLine(@"An error occured whilst attempting to load the default language files.");
	            LoadEmbeddedLanguage();
            }
        }

		/// <summary>Loads the embedded default language</summary>
		private static void LoadEmbeddedLanguage()
		{
			NewLanguage l = new NewLanguage(Resource.en_US);
			AvailableNewLanguages.Add("en-US", l);
			CurrentLanguageCode = "en-US";
		}

		internal static readonly Dictionary<string, NewLanguage> AvailableNewLanguages = new Dictionary<string, NewLanguage>();
	}
}
