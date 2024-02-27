using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace StrongNamer
{

#if NET6_0_OR_GREATER
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
	public class CleanStrongName : Task {
		[Required]
		public ITaskItem SignedAssemblyFolder { get; set; }

		public override bool Execute() {
			if (string.IsNullOrEmpty(SignedAssemblyFolder?.ItemSpec))
				return true;

			if (Directory.Exists(SignedAssemblyFolder.ItemSpec)) {
				foreach (var file in Directory.GetFiles(SignedAssemblyFolder.ItemSpec, "*.dll"))
					File.Delete(file);
			}

			return true;
		}
	}
	#if NET6_0_OR_GREATER
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
	public class VerifyCopiedStrongName : Task {
		[Required]
		public ITaskItem SignedAssemblyFolder { get; set; }
		[Required]
		public ITaskItem OutputPathDir { get; set; }
		public ITaskItem[] CopyLocalFiles { get; set; }

		public override bool Execute() {
			Log.LogMessage(MessageImportance.Low, $"VerifyCopied StrongNamer called with: {SignedAssemblyFolder?.ItemSpec} and {OutputPathDir?.ItemSpec}");
			if (string.IsNullOrEmpty(SignedAssemblyFolder?.ItemSpec) || string.IsNullOrEmpty(OutputPathDir?.ItemSpec) || ! Directory.Exists(SignedAssemblyFolder.ItemSpec))
				return true;
			var fixedFiles = new List<string>();
			//Debugger.Launch();
			foreach (var file in Directory.GetFiles(SignedAssemblyFolder.ItemSpec, "*.dll")) {
				var inputInfo = new FileInfo(file);
				var outputPath = Path.Combine(OutputPathDir.ItemSpec, inputInfo.Name);
				var outputInfo = new FileInfo(outputPath);
				try {
					if (outputInfo.Exists && outputInfo.Length != inputInfo.Length) {
						File.Copy(inputInfo.FullName, outputPath,true);
						fixedFiles.Add(inputInfo.Name);
					}
				} catch(Exception ex) {
					Log.LogWarning($"Error overriding file: {outputPath} due to: {ex}");
				}
				
			}
			if (fixedFiles.Count > 0) {
				Log.LogMessage(MessageImportance.Low, $"StrongNamer post build found unsigned file(s) in output dir that we did sign they were: {String.Join(", ",fixedFiles)}");
			}

			return true;
		}
	}
#if NET6_0_OR_GREATER
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
	public class AddStrongName : Task
	{
		[Required]
		public ITaskItem[] Assemblies { get; set; }

		[Required]
		public ITaskItem SignedAssemblyFolder { get; set; }

		public ITaskItem[] CopyLocalFiles { get; set; }

		[Output]
		public ITaskItem[] SignedAssembliesToReference { get; set; }

		[Output]
		public ITaskItem[] NewCopyLocalFiles { get; set; }

		[Required]
		public ITaskItem KeyFile { get; set; }

		public override bool Execute()
		{
			if (Assemblies == null || Assemblies.Length == 0)
			{
				return true;
			}

			if (SignedAssemblyFolder == null || string.IsNullOrEmpty(SignedAssemblyFolder.ItemSpec))
			{
				Log.LogError($"{nameof(SignedAssemblyFolder)} not specified");
				return false;
			}

			if (KeyFile == null || string.IsNullOrEmpty(KeyFile.ItemSpec))
			{
				Log.LogError("KeyFile not specified");
				return false;
			}

			if (!File.Exists(KeyFile.ItemSpec))
			{
				Log.LogError($"KeyFile not found: ${KeyFile.ItemSpec}");
				return false;
			}
			var resolverPaths = Assemblies.Select(a => a.ItemSpec).ToList();
			Log.LogMessage(MessageImportance.Normal, $"Running StrongNamer task signed assembly target folder: {SignedAssemblyFolder.ItemSpec} key file: {KeyFile.ItemSpec} CopyLocal files: {String.Join(", ",CopyLocalFiles?.Select(a=>a?.ItemSpec) ?? new string[0])} and assembly search paths: {String.Join(", ",resolverPaths)}");
			
			var refOnlyAsms = resolverPaths.Where(a => a.Contains("/ref/") || a.Contains(@"\ref\")).ToArray();
			var forcedRefPaths = new Dictionary<string, string>();
			if (refOnlyAsms.Length > 0) {
				var copyLocalPaths = CopyLocalFiles?.Select(a => a?.ItemSpec).ToArray();
				if (copyLocalPaths != null) {
					foreach (var refAsm in refOnlyAsms) {
						var fName = new FileInfo(refAsm).Name;
						var matchingCopyLocal = copyLocalPaths.FirstOrDefault(a => a.EndsWith($@"/{fName}",StringComparison.CurrentCultureIgnoreCase) || a.EndsWith(@$"\{fName}",StringComparison.CurrentCultureIgnoreCase));
						if (matchingCopyLocal != null) {
							resolverPaths[resolverPaths.IndexOf(refAsm)] = matchingCopyLocal;
							forcedRefPaths[refAsm] = matchingCopyLocal;							
						}
							Log.LogMessage(MessageImportance.High, $@"Detected a reference assembly of: {refAsm} ({fName}) hopefully found a redirect to: {matchingCopyLocal}");
					}
					Log.LogMessage(MessageImportance.Low, $@"Final assembly search paths: {String.Join(", ", resolverPaths)}");
				}
			}

				var keyBytes = File.ReadAllBytes(KeyFile.ItemSpec);

			SignedAssembliesToReference = new ITaskItem[Assemblies.Length];

			Dictionary<string, string> updatedReferencePaths = new Dictionary<string, string>();

			using (var resolver = new StrongNamerAssemblyResolver(resolverPaths))
			{
				for (int i = 0; i < Assemblies.Length; i++)
				{

					if (forcedRefPaths?.TryGetValue(Assemblies[i].ItemSpec, out var forcedPath) != true)
						forcedPath = null;
					SignedAssembliesToReference[i] = ProcessAssembly(Assemblies[i], keyBytes, resolver,forcedPath);
					if (SignedAssembliesToReference[i].ItemSpec != Assemblies[i].ItemSpec)
					{
						//  Path was updated to signed version
						updatedReferencePaths[Assemblies[i].ItemSpec] = SignedAssembliesToReference[i].ItemSpec;
					}
				}
			}

			if (CopyLocalFiles != null)
			{
				NewCopyLocalFiles = new ITaskItem[CopyLocalFiles.Length];
				for (int i = 0; i < CopyLocalFiles.Length; i++)
				{
					string updatedPath;
					if (updatedReferencePaths.TryGetValue(CopyLocalFiles[i].ItemSpec, out updatedPath))
					{
						NewCopyLocalFiles[i] = new TaskItem(CopyLocalFiles[i]);
						NewCopyLocalFiles[i].ItemSpec = updatedPath;
					}
					else
					{
						NewCopyLocalFiles[i] = CopyLocalFiles[i];
					}
				}
			}

			return true;
		}

		ITaskItem ProcessAssembly(ITaskItem assemblyItem, byte[] keyBytes, StrongNamerAssemblyResolver resolver, String ForcedNonReferenceAssemblyPath=null)
		{
			string signedAssemblyFolder = Path.GetFullPath(SignedAssemblyFolder.ItemSpec);
			if (!Directory.Exists(signedAssemblyFolder))
			{
				Directory.CreateDirectory(signedAssemblyFolder);
			}

			string assemblyOutputPath = Path.Combine(signedAssemblyFolder, Path.GetFileName(assemblyItem.ItemSpec));

			// Check if the signed assembly for this assembly already exists and the Mvid matches.
			// Avoid doing the work again if we can just re-use the existing assembly.
			// This also helps with build incrementality since we won't touch the timestamp of the signed
			// assembly if it didn't change (thus invalidating the entire build that depends on it).
			Guid existingAssemblyMvid = Guid.Empty;
			if (File.Exists(assemblyOutputPath))
			{
				try
				{
					using (var existingModule = ModuleDefinition.ReadModule(assemblyOutputPath))
					{
						existingAssemblyMvid = existingModule.Mvid;
					}
				}
				catch (Exception ex)
				{
					Log.LogMessage(MessageImportance.High, $"Assembly file '{assemblyItem.ItemSpec}' failed to load.  Skipping.  {ex}");
					throw;
				}
			}

			if (!File.Exists(assemblyItem.ItemSpec))
			{
				Log.LogMessage(MessageImportance.Low, $"Assembly file '{assemblyItem.ItemSpec}' does not exist (yet).  Skipping.");
				return assemblyItem;
			}

			using (var assembly = AssemblyDefinition.ReadAssembly(ForcedNonReferenceAssemblyPath ?? assemblyItem.ItemSpec, new ReaderParameters()
			{
				AssemblyResolver = resolver
			}))
			{
				if (assembly.Name.HasPublicKey)
				{
					Log.LogMessage(MessageImportance.Low, $"Assembly file '{assemblyItem.ItemSpec}' is already signed.  Skipping.");
					return assemblyItem;
				}

				if (existingAssemblyMvid != Guid.Empty && assembly.MainModule.Mvid == existingAssemblyMvid)
				{
					Log.LogMessage(MessageImportance.Low, $"Signed assembly already exists for '{assemblyItem.ItemSpec}' and the Mvid matches. Using existing signed assembly.");

					assemblyItem = new TaskItem(assemblyItem);
					assemblyItem.ItemSpec = assemblyOutputPath;
					return assemblyItem;
				}


				var publicKey = GetPublicKey(keyBytes);
				var token = GetKeyTokenFromKey(publicKey);

				string formattedKeyToken = BitConverter.ToString(token).Replace("-", "");
				Log.LogMessage(MessageImportance.Low, $"Signing assembly {assembly.FullName} with key with token {formattedKeyToken}");

				assembly.Name.HashAlgorithm = Mono.Cecil.AssemblyHashAlgorithm.SHA1;
				assembly.Name.PublicKey = publicKey;
				assembly.Name.HasPublicKey = true;
				assembly.Name.Attributes &= AssemblyAttributes.PublicKey;

				foreach (var reference in assembly.MainModule.AssemblyReferences.Where(r => r.PublicKeyToken == null || r.PublicKeyToken.Length == 0))
				{
					reference.PublicKeyToken = token;
					Log.LogMessage(MessageImportance.Low, $"Updating reference in assembly {assembly.FullName} to {reference.FullName} to use token {formattedKeyToken}");
				}

				string fullPublicKey = BitConverter.ToString(publicKey).Replace("-", "");

				var internalsVisibleToAttributes = assembly.CustomAttributes.Where(att => att.AttributeType.FullName == typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute).FullName).ToList();
				foreach (var internalsVisibleToAttribute in internalsVisibleToAttributes)
				{
					string internalsVisibleToAssemblyName = (string)internalsVisibleToAttribute.ConstructorArguments[0].Value;
					string newInternalsVisibleToAssemblyName = internalsVisibleToAssemblyName + ", PublicKey=" + fullPublicKey;
					Log.LogMessage(MessageImportance.Low, $"Updating InternalsVisibleToAttribute in {assembly.FullName} from {internalsVisibleToAssemblyName} to {newInternalsVisibleToAssemblyName}");

					internalsVisibleToAttribute.ConstructorArguments[0] = new CustomAttributeArgument(internalsVisibleToAttribute.ConstructorArguments[0].Type, newInternalsVisibleToAssemblyName);
				}

				Log.LogMessage(MessageImportance.Low, $"Writing signed assembly to {assemblyOutputPath}");
				try
				{
					var parameters = new WriterParameters
					{
						StrongNameKeyBlob = keyBytes,
					};

					assembly.Write(assemblyOutputPath, parameters);
				}
				catch (Exception ex)
				{
					Log.LogMessage(MessageImportance.High, $"Failed to write signed assembly to '{assemblyOutputPath}'. {ex}");
					File.Delete(assemblyOutputPath);
				}

				var ret = new TaskItem(assemblyItem);
				ret.ItemSpec = assemblyOutputPath;

				return ret;
			}
		}

		private static byte[] GetKeyTokenFromKey(byte[] fullKey)
		{
			byte[] hash;
			using (var sha1 = SHA1.Create())
			{
				hash = sha1.ComputeHash(fullKey);
			}

			return hash.Reverse().Take(8).ToArray();
		}

		// From Cecil
		// https://github.com/jbevain/cecil/pull/548/files
		static RSA CreateRSA(byte[] blob)
		{
			if (blob == null)
				throw new ArgumentNullException("blob");


			return CryptoConvert.FromCapiKeyBlob(blob);
		}

		// https://github.com/atykhyy/cecil/blob/291a779d473e9c88e597e2c9f86e47e23b49be1e/Mono.Security.Cryptography/CryptoService.cs
		public static byte[] GetPublicKey(byte[] keyBlob)
		{
			using var rsa = CreateRSA(keyBlob);

			var cspBlob = CryptoConvert.ToCapiPublicKeyBlob(rsa);
			var publicKey = new byte[12 + cspBlob.Length];
			Buffer.BlockCopy(cspBlob, 0, publicKey, 12, cspBlob.Length);
			// The first 12 bytes are documented at:
			// http://msdn.microsoft.com/library/en-us/cprefadd/html/grfungethashfromfile.asp
			// ALG_ID - Signature
			publicKey[1] = 36;
			// ALG_ID - Hash
			publicKey[4] = 4;
			publicKey[5] = 128;
			// Length of Public Key (in bytes)
			publicKey[8] = (byte)(cspBlob.Length >> 0);
			publicKey[9] = (byte)(cspBlob.Length >> 8);
			publicKey[10] = (byte)(cspBlob.Length >> 16);
			publicKey[11] = (byte)(cspBlob.Length >> 24);
			return publicKey;
		}

	}
}
