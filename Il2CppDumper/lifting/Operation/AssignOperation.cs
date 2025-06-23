namespace Il2CppDumper.lifting.Operation
{
    internal class AssignOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Source { get; set; }
        public IROperand Destination { get; set; }
    }
}