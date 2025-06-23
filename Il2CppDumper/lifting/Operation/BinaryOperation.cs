namespace Il2CppDumper.lifting.Operation
{
    internal class BinaryOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Left { get; set; }
        public IROperand Right { get; set; }
        public IROperand Destination { get; set; }
        public BinaryOperationType OperationType { get; set; }
    }
}