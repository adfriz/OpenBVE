using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using OpenBveApi.Textures;

namespace LibRender2.Textures
{
	/// <summary>Opt-in on-disk cache of upload-ready texture bytes (global, per user).</summary>
	/// <remarks>Stored under SettingsFolder/TextureCache, never inside route content.
	/// Keys bind absolute path, size, modification time and tier, so same-named
	/// textures from different routes never collide. All failures fall back to
	/// normal decoding; the cache never breaks loading.</remarks>
	public static class TextureDiskCache
	{
		/// <summary>Index schema version.</summary>
		public const int IndexVersion = 1;
		/// <summary>Baked into keys; bump to invalidate everything after policy changes.</summary>
		public const int FormatVersion = 1;
		/// <summary>Maximum cache size in bytes before oldest entries are pruned.</summary>
		public const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;
		/// <summary>Uploads smaller than this are not cached (decode is cheap anyway).</summary>
		public const int MinCacheBytes = 262144;
		private const string Magic = "OBVETC01";
		private const int HeaderLength = 32;
		private const long SaveIntervalTicks = 30L * 10000000L;

		[DataContract]
		private sealed class CacheEntry
		{
			[DataMember] public string Src;
			[DataMember] public long MtimeTicks;
			[DataMember] public long Size;
			[DataMember] public int Width;
			[DataMember] public int Height;
			[DataMember] public int Format;
			[DataMember] public int Tier;
			[DataMember] public long LastUsedTicks;
			[DataMember] public string File;
			[DataMember] public long Bytes;
		}

		[DataContract]
		private sealed class CacheIndex
		{
			[DataMember] public int Version;
			[DataMember] public Dictionary<string, CacheEntry> Entries;
		}

		private static readonly object Sync = new object();
		private static string rootFolder;
		private static string bcFolder;
		private static string indexPath;
		private static Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>();
		private static long currentBytes;
		private static bool initialized;
		private static bool available;
		private static long lastSaveTicks;

		/// <summary>Whether the cache is ready for use.</summary>
		public static bool Available
		{
			get
			{
				lock (Sync)
				{
					return initialized && available;
				}
			}
		}

		/// <summary>Initializes the cache root (idempotent, creates folders, drops temp files).</summary>
		public static void Initialize(string settingsFolder)
		{
			lock (Sync)
			{
				if (initialized)
				{
					return;
				}
				initialized = true;
				if (string.IsNullOrEmpty(settingsFolder))
				{
					return;
				}
				rootFolder = Path.Combine(settingsFolder, "TextureCache");
				bcFolder = Path.Combine(rootFolder, "bc");
				indexPath = Path.Combine(rootFolder, "index.json");
				Directory.CreateDirectory(rootFolder);
				Directory.CreateDirectory(bcFolder);
				foreach (string temp in Directory.GetFiles(rootFolder, "*.tmp"))
				{
					TryDeleteFile(temp);
				}
				LoadIndex();
				available = true;
			}
		}

		/// <summary>Loads upload-ready bytes for a source file and tier, or returns false.</summary>
		public static bool TryLoadTexture(string sourcePath, int tier, out Texture texture)
		{
			texture = null;
			string key;
			CacheEntry entry;
			lock (Sync)
			{
				if (!initialized || !available || string.IsNullOrEmpty(sourcePath))
				{
					return false;
				}
				FileInfo info;
				try
				{
					info = new FileInfo(sourcePath);
					info.Refresh();
					if (!info.Exists)
					{
						return false;
					}
					key = ComputeKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, tier);
				}
				catch
				{
					return false;
				}
				if (!entries.TryGetValue(key, out entry) || entry == null)
				{
					return false;
				}
				if (entry.MtimeTicks != info.LastWriteTimeUtc.Ticks || entry.Size != info.Length || entry.Tier != tier)
				{
					RemoveLocked(key);
					return false;
				}
				entry.LastUsedTicks = DateTime.UtcNow.Ticks;
			}
			string bcPath = ShardPath(key);
			byte[] fileBytes;
			try
			{
				fileBytes = File.ReadAllBytes(bcPath);
			}
			catch
			{
				lock (Sync)
				{
					RemoveLocked(key);
				}
				return false;
			}
			int width, height, format;
			byte[] data;
			if (!ParseFile(fileBytes, tier, out width, out height, out format, out data))
			{
				TryDeleteFile(bcPath);
				lock (Sync)
				{
					RemoveLocked(key);
				}
				return false;
			}
			try
			{
				texture = new Texture(width, height, (PixelFormat)format, data, (OpenBveApi.Colors.Color24[])null);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Stores upload-ready bytes (runs on a worker thread, never throws).</summary>
		public static void StoreUpload(string sourcePath, int tier, int width, int height, PixelFormat format, byte[] uploadBytes)
		{
			try
			{
				if (string.IsNullOrEmpty(sourcePath) || uploadBytes == null || uploadBytes.Length == 0)
				{
					return;
				}
				FileInfo info = new FileInfo(sourcePath);
				info.Refresh();
				if (!info.Exists)
				{
					return;
				}
				string key = ComputeKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, tier);
				byte[] fileBytes = BuildFile(width, height, format, tier, uploadBytes);
				string bcPath = ShardPath(key);
				Directory.CreateDirectory(Path.GetDirectoryName(bcPath));
				string tempPath = bcPath + ".tmp";
				File.WriteAllBytes(tempPath, fileBytes);
				if (File.Exists(bcPath))
				{
					File.Delete(bcPath);
				}
				File.Move(tempPath, bcPath);
				lock (Sync)
				{
					if (!initialized || !available)
					{
						return;
					}
					CacheEntry previous;
					if (entries.TryGetValue(key, out previous) && previous != null)
					{
						currentBytes -= previous.Bytes;
					}
					entries[key] = new CacheEntry
					{
						Src = info.FullName,
						MtimeTicks = info.LastWriteTimeUtc.Ticks,
						Size = info.Length,
						Width = width,
						Height = height,
						Format = (int)format,
						Tier = tier,
						LastUsedTicks = DateTime.UtcNow.Ticks,
						File = key.Substring(0, 2) + "/" + key + ".bc",
						Bytes = fileBytes.Length
					};
					currentBytes += fileBytes.Length;
					EnforceCapLocked();
					SaveIfDueLocked();
				}
			}
			catch
			{
				// cache writes must never disturb the game
			}
		}

		/// <summary>Returns entry count and total bytes (no filesystem walk).</summary>
		public static void GetStats(out int count, out long bytes)
		{
			lock (Sync)
			{
				count = entries.Count;
				bytes = currentBytes;
			}
		}

		/// <summary>Deletes every indexed entry and resets the index (safe: cache root only).</summary>
		public static void ClearAll(out int count, out long bytes)
		{
			count = 0;
			bytes = 0;
			lock (Sync)
			{
				if (!initialized || !available)
				{
					return;
				}
				foreach (CacheEntry entry in entries.Values)
				{
					if (entry == null || string.IsNullOrEmpty(entry.File))
					{
						continue;
					}
					string full = CanonicalUnderRoot(entry.File);
					if (full == null)
					{
						continue;
					}
					if (TryDeleteFile(full))
					{
						count++;
					}
				}
				bytes = currentBytes;
				entries.Clear();
				currentBytes = 0;
				SaveIndexLocked();
			}
		}

		private static string ComputeKey(string canonicalPath, long size, long mtimeTicks, int tier)
		{
			string raw = canonicalPath + "|" + size + "|" + mtimeTicks + "|" + tier + "|v" + FormatVersion;
			using (SHA1 sha = SHA1.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
				StringBuilder sb = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		private static string ShardPath(string key)
		{
			return Path.Combine(Path.Combine(bcFolder, key.Substring(0, 2)), key + ".bc");
		}

		/// <summary>Resolves a stored relative path, or null when it escapes the cache root.</summary>
		private static string CanonicalUnderRoot(string relative)
		{
			try
			{
				string full = Path.GetFullPath(Path.Combine(rootFolder, relative));
				string root = Path.GetFullPath(rootFolder);
				if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
				{
					root += Path.DirectorySeparatorChar;
				}
				bool inside = full.StartsWith(root, Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
				return inside ? full : null;
			}
			catch
			{
				return null;
			}
		}

		private static byte[] BuildFile(int width, int height, PixelFormat format, int tier, byte[] data)
		{
			byte[] fileBytes = new byte[HeaderLength + data.Length];
			Encoding.ASCII.GetBytes(Magic).CopyTo(fileBytes, 0);
			WriteInt32(fileBytes, 8, FormatVersion);
			WriteInt32(fileBytes, 12, width);
			WriteInt32(fileBytes, 16, height);
			WriteInt32(fileBytes, 20, (int)format);
			WriteInt32(fileBytes, 24, tier);
			WriteInt32(fileBytes, 28, data.Length);
			data.CopyTo(fileBytes, HeaderLength);
			return fileBytes;
		}

		private static bool ParseFile(byte[] fileBytes, int wantTier, out int width, out int height, out int format, out byte[] data)
		{
			width = 0;
			height = 0;
			format = 0;
			data = null;
			if (fileBytes == null || fileBytes.Length < HeaderLength)
			{
				return false;
			}
			for (int i = 0; i < Magic.Length; i++)
			{
				if (fileBytes[i] != (byte)Magic[i])
				{
					return false;
				}
			}
			if (ReadInt32(fileBytes, 8) != FormatVersion)
			{
				return false;
			}
			width = ReadInt32(fileBytes, 12);
			height = ReadInt32(fileBytes, 16);
			format = ReadInt32(fileBytes, 20);
			int tier = ReadInt32(fileBytes, 24);
			int dataLength = ReadInt32(fileBytes, 28);
			if (tier != wantTier || width < 1 || width > 16384 || height < 1 || height > 16384)
			{
				return false;
			}
			if (format < (int)PixelFormat.Grayscale || format > (int)PixelFormat.Paletted)
			{
				return false;
			}
			int expected = width * height * ((PixelFormat)format).BytesPerPixel();
			if (expected <= 0 || dataLength != expected || dataLength != fileBytes.Length - HeaderLength)
			{
				return false;
			}
			data = new byte[dataLength];
			Array.Copy(fileBytes, HeaderLength, data, 0, dataLength);
			return true;
		}

		private static void WriteInt32(byte[] buffer, int offset, int value)
		{
			buffer[offset] = (byte)value;
			buffer[offset + 1] = (byte)(value >> 8);
			buffer[offset + 2] = (byte)(value >> 16);
			buffer[offset + 3] = (byte)(value >> 24);
		}

		private static int ReadInt32(byte[] buffer, int offset)
		{
			return buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
		}

		private static void RemoveLocked(string key)
		{
			CacheEntry previous;
			if (entries.TryGetValue(key, out previous) && previous != null)
			{
				currentBytes -= previous.Bytes;
				entries.Remove(key);
			}
		}

		private static bool TryDeleteFile(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
					return true;
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static void EnforceCapLocked()
		{
			while (currentBytes > MaxCacheBytes && entries.Count > 0)
			{
				string oldest = null;
				long oldestTicks = long.MaxValue;
				foreach (KeyValuePair<string, CacheEntry> pair in entries)
				{
					if (pair.Value != null && pair.Value.LastUsedTicks < oldestTicks)
					{
						oldestTicks = pair.Value.LastUsedTicks;
						oldest = pair.Key;
					}
				}
				if (oldest == null)
				{
					break;
				}
				string full = CanonicalUnderRoot(oldest.Substring(0, 2) + "/" + oldest + ".bc");
				if (full != null)
				{
					TryDeleteFile(full);
				}
				RemoveLocked(oldest);
			}
		}

		private static void SaveIfDueLocked()
		{
			long now = DateTime.UtcNow.Ticks;
			if (now - lastSaveTicks < SaveIntervalTicks)
			{
				return;
			}
			lastSaveTicks = now;
			SaveIndexLocked();
		}

		private static void LoadIndex()
		{
			entries = new Dictionary<string, CacheEntry>();
			currentBytes = 0;
			try
			{
				if (!File.Exists(indexPath))
				{
					return;
				}
				using (FileStream stream = File.OpenRead(indexPath))
				{
					DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CacheIndex));
					CacheIndex index = serializer.ReadObject(stream) as CacheIndex;
					if (index == null || index.Version != IndexVersion || index.Entries == null)
					{
						return;
					}
					foreach (KeyValuePair<string, CacheEntry> pair in index.Entries)
					{
						if (pair.Value == null || string.IsNullOrEmpty(pair.Key))
						{
							continue;
						}
						entries[pair.Key] = pair.Value;
						currentBytes += Math.Max(0, pair.Value.Bytes);
					}
				}
			}
			catch
			{
				entries = new Dictionary<string, CacheEntry>();
				currentBytes = 0;
			}
		}

		private static void SaveIndexLocked()
		{
			try
			{
				CacheIndex index = new CacheIndex { Version = IndexVersion, Entries = entries };
				string tempPath = indexPath + ".tmp";
				using (FileStream stream = File.Create(tempPath))
				{
					DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CacheIndex));
					serializer.WriteObject(stream, index);
				}
				if (File.Exists(indexPath))
				{
					File.Delete(indexPath);
				}
				File.Move(tempPath, indexPath);
			}
			catch
			{
				// index loss only costs recompression next run
			}
		}
	}
}
