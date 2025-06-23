namespace Il2CppDumper.lifting.Operation
{
    internal class JumpOperation : IROperation
    {
        public ulong? Address { get; set; }
        public string TargetLabel { get; set; }
    }
}