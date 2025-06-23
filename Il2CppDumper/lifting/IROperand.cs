using Mono.Cecil;

namespace Il2CppDumper.lifting
{
    internal interface IROperand
    {
        TypeReference Type { get; set; }
    }
}