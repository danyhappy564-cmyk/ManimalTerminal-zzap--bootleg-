using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Manimal.Terminal;

internal static class TerminalNativeAssets
{
	internal sealed class NativeFile
	{
		public string Path;

		public string Sha256;
	}

	internal sealed class Contract
	{
		public int Schema;

		public string BundleSha256;

		public NativeFile[] NativeFiles;
	}

	private static readonly Dictionary<string, Task> Pending = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);

	private static readonly Regex NativeName = new Regex("\\A(?:sharedassets[0-9]+|resources)\\.(?:assets(?:\\.resS)?|resource)\\z", RegexOptions.CultureInvariant);

	private static readonly Regex HashText = new Regex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant);

	internal static bool IsNativeBundle(string path)
	{
		return path.EndsWith(".native.bundle", StringComparison.OrdinalIgnoreCase);
	}

	internal static void Prepare(string bundlePath, string dataPath)
	{
		if (IsNativeBundle(bundlePath) && !Pending.ContainsKey(bundlePath))
		{
			Pending.Add(bundlePath, Task.Run(() =>
			{
				Verify(bundlePath, dataPath);
			}));
		}
	}

	internal static void RequireVerified(string bundlePath, string dataPath)
	{
		if (IsNativeBundle(bundlePath))
		{
			Prepare(bundlePath, dataPath);
			Pending[bundlePath].GetAwaiter().GetResult();
		}
	}

	internal static void Verify(string bundlePath, string dataPath)
	{
		string path = Path.ChangeExtension(bundlePath, ".json");
		if (!File.Exists(path))
		{
			throw new InvalidDataException("Terminal native-asset manifest is missing: " + Path.GetFileName(path));
		}
		Contract contract = JsonConvert.DeserializeObject<Contract>(File.ReadAllText(path));
		Validate(contract);
		VerifyFile(bundlePath, contract.BundleSha256);
		for (int i = 0; i < contract.NativeFiles.Length; i++)
		{
			NativeFile nativeFile = contract.NativeFiles[i];
			VerifyFile(Path.Combine(dataPath, nativeFile.Path), nativeFile.Sha256);
		}
	}

	internal static void Validate(Contract contract)
	{
		if (contract == null || contract.Schema != 1 || contract.BundleSha256 == null || !HashText.IsMatch(contract.BundleSha256) || contract.NativeFiles == null || contract.NativeFiles.Length == 0)
		{
			throw new InvalidDataException("Invalid Terminal native-asset manifest");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < contract.NativeFiles.Length; i++)
		{
			NativeFile nativeFile = contract.NativeFiles[i];
			if (nativeFile == null || nativeFile.Path == null || !NativeName.IsMatch(nativeFile.Path) || !hashSet.Add(nativeFile.Path) || nativeFile.Sha256 == null || !HashText.IsMatch(nativeFile.Sha256))
			{
				throw new InvalidDataException("Invalid Terminal native-asset dependency");
			}
		}
	}

	private static void VerifyFile(string path, string expectedHash)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("Terminal requires the matching SPT client data: " + fileInfo.Name, path);
		}
		long length = fileInfo.Length;
		long ticks = fileInfo.LastWriteTimeUtc.Ticks;
		string a;
		using (FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using SHA256 sHA = SHA256.Create();
			a = BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "");
		}
		if (!string.Equals(a, expectedHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Terminal native assets do not match this content build: " + fileInfo.Name + ". Install the matching SPT client and Terminal content.");
		}
		fileInfo.Refresh();
		if (!fileInfo.Exists || fileInfo.Length != length || fileInfo.LastWriteTimeUtc.Ticks != ticks)
		{
			throw new IOException("Terminal asset file changed during verification: " + fileInfo.Name);
		}
	}
}
