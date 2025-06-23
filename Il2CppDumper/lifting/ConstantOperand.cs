using Mono.Cecil;

namespace Il2CppDumper.lifting
{
    internal class ConstantOperand : IROperand
    {
        public object Value { get; set; }
        public TypeReference Type { get; set; }
    }
}