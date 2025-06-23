using Il2CppDumper.lifting.Operation;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace Il2CppDumper.lifting
{
    internal class CilGenerator
    {
        private readonly ILProcessor ilProcessor;
        private readonly MethodDefinition method;
        private readonly Dictionary<string, VariableDefinition> registers = new();
        private readonly Dictionary<string, Instruction> labels = new();

        public CilGenerator(ILProcessor ilProcessor, MethodDefinition method)
        {
            this.ilProcessor = ilProcessor;
            this.method = method;
        }

        public void Generate(List<IROperation> operations)
        {
            // Создаем метки
            foreach (var op in operations)
            {
                if (op is JumpOperation jump)
                {
                    labels[jump.TargetLabel] = Instruction.Create(OpCodes.Nop);
                }
                else if (op is ConditionalJumpOperation condJump)
                {
                    labels[condJump.TargetLabel] = Instruction.Create(OpCodes.Nop);
                }
            }

            // Генерируем код
            foreach (var op in operations)
            {
                switch (op)
                {
                    case AssignOperation assign:
                        GenerateAssign(assign);
                        break;
                    case BinaryOperation binary:
                        GenerateBinary(binary);
                        break;
                    case CallOperation call:
                        GenerateCall(call);
                        break;
                    case CompareOperation cmp:
                        GenerateCompare(cmp);
                        break;
                    case JumpOperation jump:
                        GenerateJump(jump);
                        break;
                    case ConditionalJumpOperation condJump:
                        GenerateConditionalJump(condJump);
                        break;
                    case PushOperation push:
                        GeneratePush(push);
                        break;
                    case PopOperation pop:
                        GeneratePop(pop);
                        break;
                    case ReturnOperation ret:
                        GenerateReturn(ret);
                        break;
                }
            }

            // Добавляем возврат если отсутствует
            if (ilProcessor.Body.Instructions.Count == 0 ||
                ilProcessor.Body.Instructions[^1].OpCode != OpCodes.Ret)
            {
                ilProcessor.Emit(OpCodes.Ret);
            }
        }

        private void GenerateAssign(AssignOperation assign)
        {
            LoadOperand(assign.Source);
            StoreOperand(assign.Destination);
        }

        private void GenerateBinary(BinaryOperation binary)
        {
            LoadOperand(binary.Left);
            LoadOperand(binary.Right);

            switch (binary.OperationType)
            {
                case BinaryOperationType.Add: ilProcessor.Emit(OpCodes.Add); break;
                case BinaryOperationType.Sub: ilProcessor.Emit(OpCodes.Sub); break;
                case BinaryOperationType.Mul: ilProcessor.Emit(OpCodes.Mul); break;
                case BinaryOperationType.Div: ilProcessor.Emit(OpCodes.Div); break;
                case BinaryOperationType.Rem: ilProcessor.Emit(OpCodes.Rem); break;
                case BinaryOperationType.And: ilProcessor.Emit(OpCodes.And); break;
                case BinaryOperationType.Or: ilProcessor.Emit(OpCodes.Or); break;
                case BinaryOperationType.Xor: ilProcessor.Emit(OpCodes.Xor); break;
                case BinaryOperationType.Shl: ilProcessor.Emit(OpCodes.Shl); break;
                case BinaryOperationType.Shr: ilProcessor.Emit(OpCodes.Shr); break;
                case BinaryOperationType.Equal: ilProcessor.Emit(OpCodes.Ceq); break;
                case BinaryOperationType.NotEqual:
                    ilProcessor.Emit(OpCodes.Ceq);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    break;
                case BinaryOperationType.GreaterThan: ilProcessor.Emit(OpCodes.Cgt); break;
                case BinaryOperationType.LessThan: ilProcessor.Emit(OpCodes.Clt); break;
            }

            StoreOperand(binary.Destination);
        }

        private void GenerateCall(CallOperation call)
        {
            foreach (var arg in call.Arguments)
            {
                LoadOperand(arg);
            }

            if (call.TargetAddress.HasValue)
            {
                var methodRef = ResolveMethod(call.TargetAddress.Value);
                ilProcessor.Emit(call.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, methodRef);
            }
            else if (call.Target != null)
            {
                LoadOperand(call.Target);
                ilProcessor.Emit(OpCodes.Callvirt, method.Module.ImportReference(call.Method));
            }
            else
            {
                ilProcessor.Emit(OpCodes.Call, method.Module.ImportReference(call.Method));
            }

            if (call.Destination != null)
            {
                StoreOperand(call.Destination);
            }
        }

        private MethodReference ResolveMethod(uint rva)
        {
            return method.Module.LookupToken((int)rva) as MethodReference
                ?? throw new InvalidOperationException($"Method at RVA 0x{rva:X} not found");
        }

        private void GenerateCompare(CompareOperation cmp)
        {
            LoadOperand(cmp.Left);
            LoadOperand(cmp.Right);

            switch (cmp.OperationType)
            {
                case CompareOperationType.Equal: ilProcessor.Emit(OpCodes.Ceq); break;
                case CompareOperationType.NotEqual:
                    ilProcessor.Emit(OpCodes.Ceq);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    break;
                case CompareOperationType.GreaterThan: ilProcessor.Emit(OpCodes.Cgt); break;
                case CompareOperationType.LessThan: ilProcessor.Emit(OpCodes.Clt); break;
            }

            StoreOperand(cmp.Destination);
        }

        private void GenerateJump(JumpOperation jump)
        {
            ilProcessor.Emit(OpCodes.Br, labels[jump.TargetLabel]);
        }

        private void GenerateConditionalJump(ConditionalJumpOperation condJump)
        {
            LoadOperand(condJump.Condition);

            switch (condJump.Code)
            {
                case ConditionCode.Equal:
                    ilProcessor.Emit(OpCodes.Brtrue, labels[condJump.TargetLabel]);
                    break;
                case ConditionCode.NotEqual:
                    ilProcessor.Emit(OpCodes.Brfalse, labels[condJump.TargetLabel]);
                    break;
                    // Добавьте другие условия по аналогии
            }
        }

        private void GeneratePush(PushOperation push)
        {
            LoadOperand(push.Value);
            ilProcessor.Emit(OpCodes.Stloc, GetRegister("stack", push.Value.Type));
        }

        private void GeneratePop(PopOperation pop)
        {
            ilProcessor.Emit(OpCodes.Ldloc, GetRegister("stack", pop.Destination.Type));
            StoreOperand(pop.Destination);
        }

        private void GenerateReturn(ReturnOperation ret)
        {
            if (ret.Value != null)
            {
                LoadOperand(ret.Value);
            }
            ilProcessor.Emit(OpCodes.Ret);
        }

        private void LoadOperand(IROperand operand)
        {
            switch (operand)
            {
                case RegisterOperand reg:
                    ilProcessor.Emit(OpCodes.Ldloc, GetRegister(reg.Name, reg.Type));
                    break;
                case ConstantOperand con:
                    EmitConstant(con.Value);
                    break;
                case MemoryOperand mem:
                    GenerateMemoryLoad(mem);
                    break;
            }
        }

        private void StoreOperand(IROperand operand)
        {
            switch (operand)
            {
                case RegisterOperand reg:
                    ilProcessor.Emit(OpCodes.Stloc, GetRegister(reg.Name, reg.Type));
                    break;
                case MemoryOperand mem:
                    GenerateMemoryStore(mem);
                    break;
            }
        }

        private void GenerateMemoryLoad(MemoryOperand mem)
        {
            if (mem.Base != null)
            {
                LoadOperand(mem.Base);
                if (mem.Displacement != 0)
                {
                    ilProcessor.Emit(OpCodes.Ldc_I4, mem.Displacement);
                    ilProcessor.Emit(OpCodes.Add);
                }
            }
            else
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, mem.Displacement);
            }
            ilProcessor.Emit(OpCodes.Ldobj, method.Module.ImportReference(mem.Type));
        }

        private void GenerateMemoryStore(MemoryOperand mem)
        {
            if (mem.Base != null)
            {
                LoadOperand(mem.Base);
                if (mem.Displacement != 0)
                {
                    ilProcessor.Emit(OpCodes.Ldc_I4, mem.Displacement);
                    ilProcessor.Emit(OpCodes.Add);
                }
            }
            else
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, mem.Displacement);
            }
            ilProcessor.Emit(OpCodes.Stobj, method.Module.ImportReference(mem.Type));
        }

        private void EmitConstant(object value)
        {
            switch (value)
            {
                case int i: ilProcessor.Emit(OpCodes.Ldc_I4, i); break;
                case long l: ilProcessor.Emit(OpCodes.Ldc_I8, l); break;
                case float f: ilProcessor.Emit(OpCodes.Ldc_R4, f); break;
                case double d: ilProcessor.Emit(OpCodes.Ldc_R8, d); break;
                case bool b: ilProcessor.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); break;
                case string s: ilProcessor.Emit(OpCodes.Ldstr, s); break;
                case null: ilProcessor.Emit(OpCodes.Ldnull); break;
            }
        }

        private VariableDefinition GetRegister(string name, TypeReference type)
        {
            if (!registers.TryGetValue(name, out var variable))
            {
                variable = new VariableDefinition(method.Module.ImportReference(type));
                method.Body.Variables.Add(variable);
                registers[name] = variable;
            }
            return variable;
        }
    }
}