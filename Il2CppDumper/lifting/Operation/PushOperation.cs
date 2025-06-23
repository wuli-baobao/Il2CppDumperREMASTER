namespace Il2CppDumper.lifting.Operation
{
    internal class PushOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Value { get; set; }
    }
}