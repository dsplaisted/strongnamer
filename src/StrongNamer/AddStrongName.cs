﻿using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StrongNamer
{
    public class AddStrongName : Microsoft.Build.Utilities.Task
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

        public ITaskItem KeyName { get; set; }

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

            StrongNameKeyPair key;
            if (KeyName != null && !string.IsNullOrEmpty(KeyName.ItemSpec))
            {
                key = new StrongNameKeyPair(KeyName.ItemSpec);
            }
            else
            {
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

                using (var keyStream = File.OpenRead(KeyFile.ItemSpec))
                {
                    key = new StrongNameKeyPair(keyStream);
                }
            }

            SignedAssembliesToReference = new ITaskItem[Assemblies.Length];

            Dictionary<string, string> updatedReferencePaths = new Dictionary<string, string>();

            using (var resolver = new StrongNamerAssemblyResolver(Assemblies.Select(a => a.ItemSpec)))
            {
                for (int i = 0; i < Assemblies.Length; i++)
                {
                    SignedAssembliesToReference[i] = ProcessAssembly(Assemblies[i], key, resolver);
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
                for (int i=0; i< CopyLocalFiles.Length; i++)
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

        ITaskItem ProcessAssembly(ITaskItem assemblyItem, StrongNameKeyPair key, StrongNamerAssemblyResolver resolver)
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

            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyItem.ItemSpec, new ReaderParameters()
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

                var token = GetKeyTokenFromKey(key.PublicKey);

                string formattedKeyToken = BitConverter.ToString(token).Replace("-", "");
                Log.LogMessage(MessageImportance.Low, $"Signing assembly {assembly.FullName} with key with token {formattedKeyToken}");

                assembly.Name.HashAlgorithm = AssemblyHashAlgorithm.SHA1;
                assembly.Name.PublicKey = key.PublicKey;
                assembly.Name.HasPublicKey = true;
                assembly.Name.Attributes &= AssemblyAttributes.PublicKey;

                foreach (var reference in assembly.MainModule.AssemblyReferences.Where(r => r.PublicKeyToken == null || r.PublicKeyToken.Length == 0))
                {
                    reference.PublicKeyToken = token;
                    Log.LogMessage(MessageImportance.Low, $"Updating reference in assembly {assembly.FullName} to {reference.FullName} to use token {formattedKeyToken}");
                }

                string fullPublicKey = BitConverter.ToString(key.PublicKey).Replace("-", "");

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
                    assembly.Write(assemblyOutputPath, new WriterParameters()
                    {
                        StrongNameKeyPair = key
                    });
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
    }
}
