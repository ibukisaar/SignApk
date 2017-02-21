using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;

namespace SignApk {
	public static class Signature {
		private static readonly Regex sfRegex = new Regex(@"^META-INF/(.*)\.SF$", RegexOptions.Compiled | RegexOptions.Singleline);

		public static string FindSFName(ZipFile zip) {
			foreach (ZipEntry entry in zip) {
				Match match = sfRegex.Match(entry.Name);
				if (match.Success) {
					return match.Groups[1].Value;
				}
			}

			return null;
		}

		private static void ZipDeleteFile(ZipFile zip, string file) {
			zip.Delete(file);
		}

		public static void ClearMETAINF(ZipFile zip, string sfName) {
			ZipDeleteFile(zip, "META-INF/" + sfName + ".SF");
			ZipDeleteFile(zip, "META-INF/" + sfName + ".RSA");
			ZipDeleteFile(zip, "META-INF/" + sfName + ".DSA");
		}

		public static Manifest GetManifest(ZipFile zip, string inputDir, IEnumerable<string> files, out byte[] manifestData) {
			ZipEntry manifestEntry = zip.GetEntry("META-INF/MANIFEST.MF");

			if (manifestEntry == null) {
				return GetManifestForAll(zip, inputDir, files, out manifestData);
			}

			Stream oldManifestStream = zip.GetInputStream(manifestEntry);
			Manifest oldManifest = new Manifest(oldManifestStream);

			using (HashAlgorithm hash = HashAlgorithm.Create("SHA1")) {
				foreach (var file in files) {
					using (var temp = File.OpenRead(inputDir + '\\' + file)) {
						byte[] hashValue = hash.ComputeHash(temp);
						string key = file.Replace('\\', '/');
						string value = Convert.ToBase64String(hashValue);
						oldManifest.Map[key] = value;
					}
				}
			}

			MemoryStream newManifestStream = new MemoryStream((int) manifestEntry.Size);
			oldManifest.WriteTo(newManifestStream);
			manifestData = newManifestStream.ToArray();

			oldManifestStream.Close();
			return oldManifest;
		}

		private static Manifest GetManifestForAll(ZipFile zip, string inputDir, IEnumerable<string> files, out byte[] manifestData) {
			Dictionary<string, string> map = new Dictionary<string, string>();
			HashSet<string> filesSet = new HashSet<string>(files.Select(file => file.Replace('\\', '/')));

			using (ZipInputStream zipInput = new ZipInputStream(File.OpenRead(zip.Name))) {
				ZipEntry entry;
				using (HashAlgorithm hash = HashAlgorithm.Create("SHA1")) {
					while ((entry = zipInput.GetNextEntry()) != null) {
						byte[] hashValue;
						if (filesSet.Contains(entry.Name)) {
							using (Stream temp = File.OpenRead(inputDir + '\\' + entry.Name)) {
								hashValue = hash.ComputeHash(temp);
							}
						} else {
							hashValue = hash.ComputeHash(zipInput);
						}

						map.Add(entry.Name, Convert.ToBase64String(hashValue));
					}
				}
			}

			MemoryStream manifestStream = new MemoryStream(10 * 1024);
			Manifest manifest = new Manifest(map);
			manifest.WriteTo(manifestStream);
			manifestData = manifestStream.ToArray();

			return manifest;
		}

		public static byte[] GetSFData(ZipFile zip, string inputDir, IEnumerable<string> files, Manifest mf, byte[] mfData, string sfName) {
			ZipEntry sfEntry = zip.GetEntry("META-INF/" + sfName + ".SF");
			HashAlgorithm sha1 = HashAlgorithm.Create("SHA1");
			StringBuilder buffer = new StringBuilder(1024);
			Manifest sf;

			if (sfEntry == null) {
				Dictionary<string, string> sfMap = new Dictionary<string, string>(mf.Map.Count);
				foreach (var kv in mf.Map) {
					sfMap[kv.Key] = ComputeSha1Base64ForSF(sha1, buffer, kv.Key, kv.Value);
				}
				sf = new Manifest(sfMap);
			} else {
				var sfInputStream = zip.GetInputStream(sfEntry);
				sf = new Manifest(sfInputStream);
				foreach (var file in files) {
					sf.Map[file] = ComputeSha1Base64ForSF(sha1, buffer, file, mf.Map[file]);
				}
				sfInputStream.Close();
			}

			sf.Header =
$@"Signature-Version: 1.0
SHA1-Digest-Manifest: {Convert.ToBase64String(sha1.ComputeHash(mfData))}
Created-By: 1.0 (Saar Tool)
";
			MemoryStream sfStream = new MemoryStream(mfData.Length + 500);
			sf.WriteTo(sfStream);

			sha1.Clear();

			return sfStream.ToArray();
		}

		private static string ComputeSha1Base64ForSF(HashAlgorithm sha1, StringBuilder buffer, string key, string value) {
			buffer.Clear();
			buffer.Append("Name: ").Append(key).Append("\r\n");
			buffer.Append("SHA1-Digest: ").Append(value).Append("\r\n");
			buffer.Append("\r\n");

			byte[] hashValue = sha1.ComputeHash(Encoding.UTF8.GetBytes(buffer.ToString()));
			return Convert.ToBase64String(hashValue);
		}

		public static byte[] GetRSAData(ZipFile zip, byte[] sfData, string rsaName, string keyFile) {
			if (!File.Exists(keyFile)) throw new NoKeyException();

			X509Certificate2 certificate = new X509Certificate2(keyFile);
			ContentInfo content = new ContentInfo(sfData);
			SignedCms signedCms = new SignedCms(content, true);
			CmsSigner signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate);
			signedCms.ComputeSignature(signer);
			return signedCms.Encode();
		}
	}
}
