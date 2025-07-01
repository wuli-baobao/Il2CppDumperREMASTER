using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace Il2CppDumper
{
    public class MyAssemblyResolver : DefaultAssemblyResolver
    {
        private readonly Dictionary<string, AssemblyDefinition> _assemblies = new Dictionary<string, AssemblyDefinition>();

        public void Register(AssemblyDefinition assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));
            var name = assembly.Name.Name;
            if (!_assemblies.ContainsKey(name))
            {
                _assemblies[name] = assembly;
                Console.WriteLine($"[MyAssemblyResolver] Зарегистрирована сборка: {name}");
            }
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (_assemblies.TryGetValue(name.Name, out var assembly))
                return assembly;
            try
            {
                return base.Resolve(name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MyAssemblyResolver] Ошибка разрешения сборки {name.Name}: {ex.Message}");
                throw;
            }
        }
    }
}
