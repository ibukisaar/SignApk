using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using System.Security.Cryptography;
using System.Diagnostics;

namespace SignApk {
	public static class ApkTool {

		private static Stream CreateFile(string file) {
			Common.TryCreateDirectory(file);
			return new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
		}

		public static int Unzip(string apkFile, IEnumerable<string> files, string outputDir) {
			outputDir = Common.StandardDirectory(outputDir);

			using (var zip = new ZipFile(apkFile)) {
				files = GetUpdateFilesForUnzip(zip, outputDir, files);
				foreach (string file in files) {
					ZipEntry entry = zip.GetEntryReadonly(file);
					Stream outputStream = CreateFile(outputDir + '\\' + entry.Name);
					Stream inputStream = zip.GetInputStream(entry);
					inputStream.CopyTo(outputStream);
					inputStream.Close();
					outputStream.Close();
					File.SetLastWriteTime(outputDir + '\\' + entry.Name, entry.DateTime);
				}
				return files.Count();
			}
		}

		private static void TryCopy(string outputApkFile, string sourceApkFile) {
			if (Common.TryCreateDirectory(outputApkFile) || !File.Exists(outputApkFile)) {
				using (Stream zip = File.Create(outputApkFile, 4096, FileOptions.SequentialScan))
				using (FileStream input = new FileStream(sourceApkFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan)) {
					input.CopyTo(zip);
				}
			}
		}

		private static IEnumerable<string> GetUpdateFilesForUnzip(ZipFile zip, string inputDir, IEnumerable<string> files) {
			List<string> result = new List<string>();
			foreach (var file in new SortedSet<string>(files)) {
				var entry = zip.GetEntryReadonly(file);
				string path = inputDir + '\\' + file;
				if (!File.Exists(path) || File.GetLastWriteTime(path) != entry.DateTime) {
					result.Add(file);
				}
			}
			return result;
		}

		private static IEnumerable<string> GetUpdateFilesForZip(ZipFile zip, string inputDir, IEnumerable<string> files) {
			List<string> result = new List<string>();
			foreach (var file in files) {
				var entry = zip.GetEntryReadonly(file);
				if (entry == null || File.GetLastWriteTime(inputDir + '\\' + file) != entry.DateTime) {
					result.Add(file);
				}
			}
			return result;
		}

		public static int ZipAndSign(string sourceApkFile, string inputDir, IEnumerable<string> files, string outputFile, string keyFile) {
			inputDir = Common.StandardDirectory(inputDir);

			bool copy;

			if (File.Exists(outputFile)) {
				using (var zip = new ZipFile(File.OpenRead(outputFile))) {
					files = GetUpdateFilesForZip(zip, inputDir, files);
				}
			}

			using (var zip = new ZipFile(File.OpenRead(sourceApkFile))) {
				if (files.Count() < zip.Count / 2) {
					long filesLengthSum = files.Sum(file => new FileInfo(inputDir + '\\' + file).Length);
					long zipFileLength = new FileInfo(sourceApkFile).Length;
					copy = filesLengthSum < zipFileLength;
				} else {
					copy = false;
				}
			}

			if (copy) {
				if (files.Count() == 0) return 0;

				string sfName;
				byte[] manifestData, sfData, rsaData;
				TryCopy(outputFile, sourceApkFile);

				using (var zip = new ZipFile(File.Open(outputFile, FileMode.Open, FileAccess.Read, FileShare.Read))) {
					sfName = Signature.FindSFName(zip) ?? "SAAR";
					Manifest manifest = Signature.GetManifest(zip, inputDir, files, out manifestData);
					sfData = Signature.GetSFData(zip, inputDir, files, manifest, manifestData, sfName);
					rsaData = Signature.GetRSAData(zip, sfData, sfName, keyFile);
				}

				using (var zip = new ZipFile(File.Open(outputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))) {
					zip.BeginUpdate();

					foreach (var file in files) {
						var entry = zip.EntryFactory.MakeFileEntry(file);
						entry.DateTime = new FileInfo(inputDir + '\\' + file).LastWriteTime;
						zip.Add(inputDir + '\\' + file, entry.Name);
					}

					zip.Add(new StaticDataSource(manifestData), "META-INF/MANIFEST.MF");
					zip.Add(new StaticDataSource(sfData), $"META-INF/{sfName}.SF");
					zip.Add(new StaticDataSource(rsaData), $"META-INF/{sfName}.RSA");

					zip.CommitUpdate();
				}
			} else {
				ZipCopyToZip(sourceApkFile, inputDir, files, outputFile);
				Sign(outputFile, inputDir, files, keyFile);
			}

			return files.Count();
		}

		private static void ZipCopyToZip(string sourceApkFile, string inputDir, IEnumerable<string> files, string outputFile) {
			HashSet<string> filesSet = new HashSet<string>(files.Select(file => file));
			using (ZipInputStream zipInput = new ZipInputStream(File.OpenRead(sourceApkFile)))
			using (ZipOutputStream zipOutput = new ZipOutputStream(File.Create(outputFile, 4096, FileOptions.SequentialScan))) {
				zipOutput.UseZip64 = UseZip64.Off;

				ZipEntryFactory factory = new ZipEntryFactory();
				ZipEntry entry;
				while ((entry = zipInput.GetNextEntry()) != null) {
					ZipEntry entry2 = factory.MakeFileEntry(entry.Name);
					entry2.DosTime = entry.DosTime;
					zipOutput.PutNextEntry(entry2);

					if (filesSet.Remove(entry.Name)) {
						using (var temp = File.OpenRead(inputDir + '\\' + entry.Name)) {
							temp.CopyTo(zipOutput);
						}
					} else {
						zipInput.CopyTo(zipOutput);
					}
				}

				foreach (var file in filesSet) {
					entry = factory.MakeFileEntry(file);
					entry.DateTime = new FileInfo(inputDir + '\\' + entry.Name).LastWriteTime;
					zipOutput.PutNextEntry(entry);
					using (var temp = File.OpenRead(inputDir + '\\' + entry.Name)) {
						temp.CopyTo(zipOutput);
					}
				}
			}
		}

		private static void Sign(string apkFile, string inputDir, IEnumerable<string> files, string keyFile) {
			inputDir = Common.StandardDirectory(inputDir);

			Stopwatch sw = new Stopwatch();
			byte[] sfData, rsaData;

			using (var zip = new ZipFile(new FileStream(apkFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))) {
				string sfName = Signature.FindSFName(zip) ?? "SAAR";

				sw.Restart();

				Manifest manifest = Signature.GetManifest(zip, inputDir, files, out byte[] manifestData);
				sfData = Signature.GetSFData(zip, inputDir, files, manifest, manifestData, sfName);
				rsaData = Signature.GetRSAData(zip, sfData, sfName, keyFile);

				sw.Stop();
				Console.WriteLine("计算签名耗时: " + sw.Elapsed);

				sw.Restart();

				zip.BeginUpdate();
				zip.Add(new StaticDataSource(manifestData), "META-INF/MANIFEST.MF");
				zip.Add(new StaticDataSource(sfData), $"META-INF/{sfName}.SF");
				zip.Add(new StaticDataSource(rsaData), $"META-INF/{sfName}.RSA");
				zip.CommitUpdate();

				sw.Stop();
				Console.WriteLine("写入签名耗时：" + sw.Elapsed);
			}
		}
	}
}
