namespace Il2CppDumper.lifting.Operation
{
    internal class ErrorOperation : IROperation
    {
        public ulong? Address { get; set; }
        public string Message { get; set; }
    }
}