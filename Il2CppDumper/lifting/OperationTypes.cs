namespace Il2CppDumper.lifting
{
    internal enum BinaryOperationType
    {
        Add, Sub, Mul, Div, Rem,
        And, Or, Xor,
        Shl, Shr,
        Equal, NotEqual,
        GreaterThan, LessThan,
        GreaterThanOrEqual, LessThanOrEqual
    }

    internal enum UnaryOperationType
    {
        Negate, Not, LogicalNot
    }

    internal enum CompareOperationType
    {
        Equal, NotEqual,
        GreaterThan, LessThan
    }

    internal enum ConditionCode
    {
        Equal, NotEqual,
        Greater, GreaterOrEqual,
        Less, LessOrEqual,
        Above, Below,
        Overflow, NoOverflow
    }
}