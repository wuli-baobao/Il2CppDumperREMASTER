namespace Il2CppDumper.lifting.Operation
{
    internal class PopOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Destination { get; set; }
    }
}