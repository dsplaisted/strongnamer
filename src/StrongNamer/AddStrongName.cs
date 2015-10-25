using Microsoft.Build.Framework;
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
        public ITaskItem[] OutputAssemblies { get; set; }

        [Required]
        public ITaskItem KeyFile { get; set; }

        public override bool Execute()
        {
            if (Assemblies == null || Assemblies.Length == 0)
            {
                return true;
            }

            if (OutputAssemblies == null)
            {
                OutputAssemblies = Assemblies;
            }
            else if (OutputAssemblies.Length != Assemblies.Length)
            {
                Log.LogError($"{nameof(OutputAssemblies)} must have the same number of items as {nameof(Assemblies)}");
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

            StrongNameKeyPair key;
            using (var keyStream = File.OpenRead(KeyFile.ItemSpec))
            {
                key = new StrongNameKeyPair(keyStream);
            }

            for (int i = 0; i < Assemblies.Length; i++)
            {
                ProcessAssembly(Assemblies[i].ItemSpec, OutputAssemblies[i].ItemSpec, key);
            }

            return true;
        }

        void ProcessAssembly(string assemblyPath, string assemblyOutputPath, StrongNameKeyPair key)
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            if (assembly.Name.HasPublicKey)
            {
                Log.LogMessage(MessageImportance.Low, $"Assembly file '{assemblyPath}' is already signed.  Skipping.");
                return;
            }

            assembly.Name.HashAlgorithm = AssemblyHashAlgorithm.SHA1;
            assembly.Name.PublicKey = key.PublicKey;
            assembly.Name.HasPublicKey = true;
            assembly.Name.Attributes &= AssemblyAttributes.PublicKey;

            var token = new Lazy<byte[]>(() =>
            {
                return GetKeyTokenFromKey(key.PublicKey);
            }, LazyThreadSafetyMode.None);

            foreach (var reference in assembly.MainModule.AssemblyReferences.Where(r => !r.HasPublicKey))
            {
                reference.PublicKeyToken = token.Value;
            }

            assembly.Write(assemblyOutputPath, new WriterParameters()
            {
                StrongNameKeyPair = key
            });
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
