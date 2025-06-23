// Этот enum определяет архитектуру для нужд дизассемблера.
// Он должен быть сопоставлен с информацией из классов Il2Cpp, PE, Elf, MachO.
public enum ArchitectureType
{
    Unknown,
    X86_32,
    X86_64,
    ARM32,
    ARM64
}
