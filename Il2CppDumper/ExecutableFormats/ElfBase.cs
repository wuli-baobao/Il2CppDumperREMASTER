using System.IO;

namespace Il2CppDumper
{
    public abstract class ElfBase : Il2Cpp
    {
        protected ushort ei_machine; // Поле для хранения типа машины

        protected ElfBase(Stream stream) : base(stream) { }
        protected abstract void Load();
        protected abstract bool CheckSection();

        public override bool CheckDump() => !CheckSection();

        public void Reload() => Load();

        public override ArchitectureType GetArchitectureType()
        {
            switch (this.ei_machine)
            {
                case Il2CppConstants.EM_386:
                    return ArchitectureType.X86_32;
                case Il2CppConstants.EM_X86_64:
                    return ArchitectureType.X86_64;
                case Il2CppConstants.EM_ARM:
                    return ArchitectureType.ARM32;
                case Il2CppConstants.EM_AARCH64:
                    return ArchitectureType.ARM64;
                default:
                    Console.WriteLine($"[Warning ElfBase.GetArchitectureType] Unknown ELF machine type: {this.ei_machine}. Returning Unknown.");
                    return ArchitectureType.Unknown;
            }
        }
    }
}
