using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;

namespace StrongNamer
{
    class StrongNamerAssemblyResolver : BaseAssemblyResolver
    {
        List<AssemblyDefinition> _assemblies = new List<AssemblyDefinition>();

        public StrongNamerAssemblyResolver(List<AssemblyDefinition> assemblies) : base()
        {
            _assemblies = assemblies;
        }

        // Check the StrongNamer knowledge of assemblies before passing to the base resolver.
        // Base resolver checks local folders and the GAC, so will not find anything in the Packages folder
        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            var matchedAssembly = _assemblies.SingleOrDefault(ad => ad.Name.Name.Equals(name.Name));
            if (matchedAssembly == null)
            {
                return base.Resolve(name, parameters);
            }
            return matchedAssembly;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters());
        }
    }
}
