using System;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Benchmark
{
    public class MainNamespace : INamespace
    {
        public MainNamespace(IAssembly DeclaringAssembly)
        {
            this.DeclaringAssembly = DeclaringAssembly;
            this.Types = new List<IType>();
        }

        public IAssembly DeclaringAssembly { get; private set; }

        public string Name { get { return ""; } }
        public string FullName { get { return Name; } }
        public IEnumerable<IAttribute> Attributes { get { return Enumerable.Empty<IAttribute>(); } }
        public List<IType> Types { get; private set; }

        IEnumerable<IType> INamespace.Types
        {
            get { return Types; }
        }
    }
}

