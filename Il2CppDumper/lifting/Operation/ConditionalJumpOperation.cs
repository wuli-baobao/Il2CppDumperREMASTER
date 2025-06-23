namespace Il2CppDumper.lifting.Operation
{
    internal class ConditionalJumpOperation : IROperation
    {
        public ulong? Address { get; set; }
        public IROperand Condition { get; set; }
        public ConditionCode Code { get; set; }
        public string TargetLabel { get; set; }
    }
}