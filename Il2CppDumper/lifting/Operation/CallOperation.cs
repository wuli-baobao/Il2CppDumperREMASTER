using Mono.Cecil;
using System.Collections.Generic;

namespace Il2CppDumper.lifting.Operation
{
    internal class CallOperation : IROperation
    {
        public ulong? Address { get; set; }
        public uint? TargetAddress { get; set; }
        public IROperand Target { get; set; }
        public MethodReference Method { get; set; }
        public MethodReference Signature { get; set; }
        public bool IsVirtual { get; set; }
        public List<IROperand> Arguments { get; set; } = new List<IROperand>();
        public IROperand Destination { get; set; }
    }
}