using System.Collections.Generic;
using Mono.Cecil;

namespace Il2CppDumper.lifting.Operation
{
    internal class CallOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Target { get; set; }
        public uint? TargetAddress { get; set; }
        public MethodReference Method { get; set; }
        public List<IROperand> Arguments { get; } = new List<IROperand>();
        public IROperand Destination { get; set; }
        public bool IsVirtual { get; set; }
    }
}