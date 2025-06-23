using Mono.Cecil;

namespace Il2CppDumper.lifting
{
    internal class RegisterOperand : IROperand
    {
        public string Name { get; set; }
        public TypeReference Type { get; set; }
    }
}