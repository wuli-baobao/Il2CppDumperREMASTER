using Gee.External.Capstone.X86;
using Il2CppDumper.lifting.Operation;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Il2CppDumper.lifting
{
    internal class Lifter
    {
        private readonly ModuleDefinition _module;
        private readonly Dictionary<string, TypeReference> _typeCache = new();
        private readonly Dictionary<ulong, string> _labelMap = new();
        private int _labelCounter;

        public Lifter(ModuleDefinition module)
        {
            _module = module;
        }

        public List<IROperation> Lift(IEnumerable<DisassembledInstruction> instructions)
        {
            var result = new List<IROperation>();
            _labelMap.Clear();
            _labelCounter = 0;

            // First pass: identify jump targets
            foreach (var instr in instructions)
            {
                if (instr.PlatformSpecificInstruction is X86Instruction x86Instr)
                {
                    ProcessJumpTargets(x86Instr);
                }
            }

            // Second pass: convert instructions
            foreach (var instr in instructions)
            {
                // Add label if current address is a jump target
                if (_labelMap.TryGetValue(instr.Address, out var label))
                {
                    result.Add(new JumpOperation { TargetLabel = label });
                }

                if (instr.PlatformSpecificInstruction is X86Instruction x86Instr)
                {
                    var lifted = LiftX86Instruction(x86Instr);
                    if (lifted != null)
                    {
                        result.AddRange(lifted);
                    }
                }
            }

            return result;
        }

        private void ProcessJumpTargets(X86Instruction instr)
        {
            if (!IsControlFlowInstruction(instr)) return;

            var detail = instr.Details;  // Fixed: use Details instead of Detail
            if (detail is X86InstructionDetail x86Detail)
            {
                foreach (var operand in x86Detail.Operands)
                {
                    if (operand.Type == X86OperandType.Immediate)
                    {
                        var targetAddr = (ulong)operand.Immediate;
                        if (!_labelMap.ContainsKey(targetAddr))
                        {
                            _labelMap[targetAddr] = $"L_{_labelCounter++}";
                        }
                    }
                }
            }
        }

        private List<IROperation> LiftX86Instruction(X86Instruction instr)
        {
            var operations = new List<IROperation>();
            var detail = instr.Details as X86InstructionDetail;  // Fixed: use Details

            if (detail == null) return operations;

            switch (instr.Mnemonic.ToUpper())
            {
                case "MOV":
                    if (detail.Operands.Length == 2)
                    {
                        operations.Add(CreateAssign(detail.Operands[0], detail.Operands[1], (ulong)instr.Address));
                    }
                    break;

                case "ADD":
                case "SUB":
                case "MUL":
                case "DIV":
                case "AND":
                case "OR":
                case "XOR":
                case "SHL":
                case "SHR":
                    if (detail.Operands.Length == 2)
                    {
                        operations.Add(CreateBinary(
                            instr.Mnemonic.ToUpper(),
                            detail.Operands[0],
                            detail.Operands[1],
                            (ulong)instr.Address
                        ));
                    }
                    break;

                case "CMP":
                    if (detail.Operands.Length == 2)
                    {
                        operations.Add(CreateCompare(detail.Operands[0], detail.Operands[1], (ulong)instr.Address));
                    }
                    break;

                case "CALL":
                    operations.Add(CreateCall(instr, detail));
                    break;

                case "RET":
                    operations.Add(new ReturnOperation { Address = (ulong?)instr.Address });
                    break;

                case "JMP":
                    operations.Add(CreateJump(instr, detail));
                    break;

                case "JE":
                case "JNE":
                case "JG":
                case "JGE":
                case "JL":
                case "JLE":
                    operations.Add(CreateConditionalJump(instr, detail));
                    break;

                case "PUSH":
                    if (detail.Operands.Length == 1)
                    {
                        operations.Add(CreatePush(detail.Operands[0], (ulong)instr.Address));
                    }
                    break;

                case "POP":
                    if (detail.Operands.Length == 1)
                    {
                        operations.Add(CreatePop(detail.Operands[0], (ulong)instr.Address));
                    }
                    break;

                case "LEA":
                    if (detail.Operands.Length == 2)
                    {
                        operations.Add(CreateLea(detail.Operands[0], detail.Operands[1], (ulong)instr.Address));
                    }
                    break;
            }

            return operations;
        }

        private AssignOperation CreateAssign(X86Operand dest, X86Operand src, ulong address)
        {
            return new AssignOperation
            {
                Address = address,
                Destination = ConvertOperand(dest),
                Source = ConvertOperand(src)
            };
        }

        private BinaryOperation CreateBinary(string mnemonic, X86Operand left, X86Operand right, ulong address)
        {
            var opType = mnemonic switch
            {
                "ADD" => BinaryOperationType.Add,
                "SUB" => BinaryOperationType.Sub,
                "MUL" => BinaryOperationType.Mul,
                "DIV" => BinaryOperationType.Div,
                "AND" => BinaryOperationType.And,
                "OR" => BinaryOperationType.Or,
                "XOR" => BinaryOperationType.Xor,
                "SHL" => BinaryOperationType.Shl,
                "SHR" => BinaryOperationType.Shr,
                _ => throw new NotSupportedException($"Unsupported binary operation: {mnemonic}")
            };

            return new BinaryOperation
            {
                Address = address,
                Left = ConvertOperand(left),
                Right = ConvertOperand(right),
                OperationType = opType,
                Destination = ConvertOperand(left)
            };
        }

        private CompareOperation CreateCompare(X86Operand left, X86Operand right, ulong address)
        {
            return new CompareOperation
            {
                Address = address,
                Left = ConvertOperand(left),
                Right = ConvertOperand(right),
                OperationType = CompareOperationType.Equal,
                Destination = new RegisterOperand { Name = "eflags", Type = _module.TypeSystem.UInt32 }
            };
        }

        private CallOperation CreateCall(X86Instruction instr, X86InstructionDetail detail)
        {
            var callOp = new CallOperation
            {
                Address = (ulong)instr.Address,
                IsVirtual = false
            };

            if (detail.Operands.Length > 0)
            {
                var targetOp = detail.Operands[0];
                if (targetOp.Type == X86OperandType.Immediate)
                {
                    callOp.TargetAddress = (uint?)targetOp.Immediate;
                }
                else
                {
                    callOp.Target = ConvertOperand(targetOp);
                }
            }

            return callOp;
        }

        private JumpOperation CreateJump(X86Instruction instr, X86InstructionDetail detail)
        {
            if (detail.Operands.Length == 0) return null;

            var targetOp = detail.Operands[0];
            ulong targetAddr = 0;

            if (targetOp.Type == X86OperandType.Immediate)
            {
                targetAddr = (ulong)targetOp.Immediate;
            }

            return new JumpOperation
            {
                Address = (ulong)instr.Address,
                TargetLabel = _labelMap.TryGetValue(targetAddr, out var label) ? label : $"L_{targetAddr:X}"
            };
        }

        private ConditionalJumpOperation CreateConditionalJump(X86Instruction instr, X86InstructionDetail detail)
        {
            if (detail.Operands.Length == 0) return null;

            var targetOp = detail.Operands[0];
            ulong targetAddr = 0;

            if (targetOp.Type == X86OperandType.Immediate)
            {
                targetAddr = (ulong)targetOp.Immediate;
            }

            var condition = new RegisterOperand { Name = "eflags", Type = _module.TypeSystem.UInt32 };

            var conditionCode = instr.Mnemonic.ToUpper() switch
            {
                "JE" => ConditionCode.Equal,
                "JNE" => ConditionCode.NotEqual,
                "JG" => ConditionCode.Greater,
                "JGE" => ConditionCode.GreaterOrEqual,
                "JL" => ConditionCode.Less,
                "JLE" => ConditionCode.LessOrEqual,
                _ => ConditionCode.Equal
            };

            return new ConditionalJumpOperation
            {
                Address = (ulong)instr.Address,
                Condition = condition,
                Code = conditionCode,
                TargetLabel = _labelMap.TryGetValue(targetAddr, out var label) ? label : $"L_{targetAddr:X}"
            };
        }

        private PushOperation CreatePush(X86Operand operand, ulong address)
        {
            return new PushOperation
            {
                Address = address,
                Value = ConvertOperand(operand)
            };
        }

        private PopOperation CreatePop(X86Operand operand, ulong address)
        {
            return new PopOperation
            {
                Address = address,
                Destination = ConvertOperand(operand)
            };
        }

        private AssignOperation CreateLea(X86Operand dest, X86Operand src, ulong address)
        {
            if (src.Type != X86OperandType.Memory)
                throw new InvalidOperationException("LEA source must be a memory operand");

            return new AssignOperation
            {
                Address = address,
                Destination = ConvertOperand(dest),
                Source = ConvertMemoryOperand(src.Memory, true)
            };
        }

        private IROperand ConvertOperand(X86Operand operand)
        {
            return operand.Type switch
            {
                X86OperandType.Register => ConvertRegisterOperand(operand.Register),
                X86OperandType.Immediate => ConvertImmediateOperand(operand),
                X86OperandType.Memory => ConvertMemoryOperand(operand.Memory),
                _ => throw new NotSupportedException($"Operand type {operand.Type} not supported")
            };
        }

        private RegisterOperand ConvertRegisterOperand(X86Register register)
        {
            return new RegisterOperand
            {
                Name = register.Name.ToLower(),
                Type = GetTypeReference(GetRegisterSize(register))
            };
        }

        private ConstantOperand ConvertImmediateOperand(X86Operand operand)
        {
            return new ConstantOperand
            {
                Value = operand.Immediate,
                Type = GetTypeReference(operand.Size)
            };
        }

        private MemoryOperand ConvertMemoryOperand(X86MemoryOperandValue memory, bool addressOnly = false)
        {
            var memOperand = new MemoryOperand
            {
                Displacement = (int)memory.Displacement,
                Type = GetTypeReference(memory.Base == null ? 0 : GetRegisterSize(memory.Base))
            };

            if (memory.Base != null)
            {
                memOperand.BaseRegister = ConvertRegisterOperand(memory.Base);
            }

            if (memory.Index != null)
            {
                memOperand.IndexRegister = ConvertRegisterOperand(memory.Index);
                memOperand.ScaleValue = memory.Scale;
            }

            if (addressOnly)
            {
                memOperand.Type = _module.TypeSystem.IntPtr;
            }

            return memOperand;
        }

        private TypeReference GetTypeReference(Type type)
        {
            var key = type.FullName;
            if (!_typeCache.TryGetValue(key, out var typeRef))
            {
                typeRef = _module.ImportReference(type);
                _typeCache[key] = typeRef;
            }
            return typeRef;
        }

        private TypeReference GetTypeReference(int sizeInBytes)
        {
            return sizeInBytes switch
            {
                1 => _module.TypeSystem.Byte,
                2 => _module.TypeSystem.UInt16,
                4 => _module.TypeSystem.UInt32,
                8 => _module.TypeSystem.UInt64,
                _ => _module.TypeSystem.IntPtr
            };
        }

        private int GetRegisterSize(X86Register register)
        {
            return register.Name.ToLower() switch
            {
                "al" or "ah" or "bl" or "bh" or "cl" or "ch" or "dl" or "dh" => 1,
                "ax" or "bx" or "cx" or "dx" or "si" or "di" or "bp" or "sp" => 2,
                "eax" or "ebx" or "ecx" or "edx" or "esi" or "edi" or "ebp" or "esp" => 4,
                "rax" or "rbx" or "rcx" or "rdx" or "rsi" or "rdi" or "rbp" or "rsp" => 8,
                _ => IntPtr.Size
            };
        }

        private bool IsControlFlowInstruction(X86Instruction instr)
        {
            string mnemonic = instr.Mnemonic.ToUpper();
            return mnemonic.StartsWith("J") ||
                   mnemonic == "CALL" ||
                   mnemonic == "RET";
        }
    }
}