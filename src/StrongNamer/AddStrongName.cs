using Microsoft.Build.Framework;
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

        [Output]
        public ITaskItem[] SignedAssembliesToReference { get; set; }

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

            StrongNameKeyPair key;
            using (var keyStream = File.OpenRead(KeyFile.ItemSpec))
            {
                key = new StrongNameKeyPair(keyStream);
            }

            SignedAssembliesToReference = new ITaskItem[Assemblies.Length];

            for (int i = 0; i < Assemblies.Length; i++)
            {
                SignedAssembliesToReference[i] = ProcessAssembly(Assemblies[i], key);
            }

            return true;
        }

        ITaskItem ProcessAssembly(ITaskItem assemblyItem, StrongNameKeyPair key)
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyItem.ItemSpec);

            if (assembly.Name.HasPublicKey)
            {
                Log.LogMessage(MessageImportance.Low, $"Assembly file '{assemblyItem.ItemSpec}' is already signed.  Skipping.");
                return assemblyItem;
            }

            if (!Directory.Exists(SignedAssemblyFolder.ItemSpec))
            {
                Directory.CreateDirectory(SignedAssemblyFolder.ItemSpec);
            }

            string assemblyOutputPath = Path.Combine(SignedAssemblyFolder.ItemSpec, Path.GetFileName(assemblyItem.ItemSpec));

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

            var ret = new TaskItem(assemblyItem);
            ret.ItemSpec = assemblyOutputPath;

            return ret;
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
