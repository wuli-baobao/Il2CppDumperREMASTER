using Il2CppDumper.lifting;
using Mono.Cecil;

internal class MemoryOperand : IROperand
{
    public IROperand Base { get; set; }
    public IROperand BaseRegister { get; set; } 
    public IROperand IndexRegister { get; set; }
    public int ScaleValue { get; set; }
    public int Displacement { get; set; }
    public TypeReference Type { get; set; }
}