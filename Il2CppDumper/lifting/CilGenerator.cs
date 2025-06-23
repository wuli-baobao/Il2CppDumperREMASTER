using Il2CppDumper.lifting.Operation;
using Il2CppDumper.UppdateLowCode;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppDumper.lifting
{
    internal class CilGenerator
    {
        private readonly ILProcessor ilProcessor;
        private readonly MethodDefinition method;
        private readonly DummyAssemblyGenerator generator;
        private readonly ModuleDefinition module;
        private readonly Dictionary<string, VariableDefinition> registers = new();
        private readonly Dictionary<string, Instruction> labels = new();
        private readonly Dictionary<ulong, Instruction> addressToInstruction = new();
        private Instruction lastGeneratedInstruction;

        public CilGenerator(ILProcessor ilProcessor, MethodDefinition method, DummyAssemblyGenerator generator)
        {
            this.ilProcessor = ilProcessor;
            this.method = method;
            this.generator = generator;
            this.module = method.Module;
        }

        public void Generate(List<IROperation> operations)
        {
            // Создаем метки
            CreateLabels(operations);

            // Оптимизация и анализ типов ВНУТРИ генератора
            var optimizedOperations = OptimizeIR(operations);

            // Генерируем код
            foreach (var op in optimizedOperations)
            {
                try
                {
                    GenerateOperation(op);
                }
                catch (Exception ex)
                {
                    // Логируем ошибку и добавляем исключение в код
                    ilProcessor.Emit(OpCodes.Ldstr, $"Operation error: {ex.Message}");
                    ilProcessor.Emit(OpCodes.Newobj, SafeImportConstructor(
                        typeof(Exception),
                        new[] { typeof(string) }));
                    ilProcessor.Emit(OpCodes.Throw);
                }
            }

            // Добавляем возврат если отсутствует
            AddReturnIfMissing();

            // Применяем метки
            ApplyLabels();
        }

        private List<IROperation> OptimizeIR(List<IROperation> operations)
        {
            try
            {
                // Data flow analysis (теперь в правильном контексте)
                var dfAnalyzer = new DataFlowAnalyzer(module);
                var typeMap = dfAnalyzer.AnalyzeTypes(operations);

                // Apply types to registers
                foreach (var op in operations.OfType<RegisterOperand>())
                {
                    if (typeMap.TryGetValue(op.Name, out var type))
                    {
                        op.Type = type;
                    }
                }

                // Optimize IR
                var optimizer = new IROptimizer();
                return optimizer.Optimize(operations, typeMap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CilGenerator] IR optimization failed: {ex}");
                return operations; // Возвращаем оригинал при ошибке
            }
        }

        private void CreateLabels(List<IROperation> operations)
        {
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
        }

        private void GenerateOperation(IROperation op)
        {
            switch (op)
            {
                case AssignOperation assign: GenerateAssign(assign); break;
                case BinaryOperation binary: GenerateBinary(binary); break;
                case CallOperation call: GenerateCall(call); break;
                case CompareOperation cmp: GenerateCompare(cmp); break;
                case JumpOperation jump: GenerateJump(jump); break;
                case ConditionalJumpOperation condJump: GenerateConditionalJump(condJump); break;
                case PushOperation push: GeneratePush(push); break;
                case PopOperation pop: GeneratePop(pop); break;
                case ReturnOperation ret: GenerateReturn(ret); break;
                case ErrorOperation error: GenerateError(error); break;
            }
        }

        private void RecordInstructionAddress(IROperation op, Instruction startInstruction)
        {
            var firstNewInstruction = startInstruction != null
                ? startInstruction.Next ?? ilProcessor.Body.Instructions.First()
                : ilProcessor.Body.Instructions.FirstOrDefault();

            if (firstNewInstruction != null)
            {
                ulong address = (ulong)op.Address;
                addressToInstruction[address] = firstNewInstruction;
            }
        }

        public Instruction GetInstruction(ulong address)
        {
            return addressToInstruction.TryGetValue(address, out var instr)
                ? instr
                : null;
        }

        public void AddExceptionHandler(ExceptionHandler handler)
        {
            ilProcessor.Body.ExceptionHandlers.Add(handler);
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
            ilProcessor.Append(CreateComment($"Call operation at 0x{call.Address:X}"));

            foreach (var arg in call.Arguments)
            {
                LoadOperand(arg);
            }

            if (call.TargetAddress.HasValue)
            {
                var methodRef = ResolveMethod(call.TargetAddress.Value);
                if (methodRef != null)
                {
                    ilProcessor.Append(CreateComment($"Call to {methodRef.FullName}"));
                    ilProcessor.Emit(call.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, methodRef);
                }
                else
                {
                    ilProcessor.Append(CreateComment($"Unresolved call to 0x{call.TargetAddress.Value:X}"));
                    ilProcessor.Emit(OpCodes.Call, SafeImportMethod(new MethodReference(
                        $"Unresolved_0x{call.TargetAddress.Value:X}",
                        module.TypeSystem.Void)));
                }
            }
            else if (call.Target != null)
            {
                ilProcessor.Append(CreateComment("Indirect call"));
                LoadOperand(call.Target);
                ilProcessor.Emit(OpCodes.Callvirt, SafeImportMethod(call.Method));
            }
            else if (call.Method != null)
            {
                ilProcessor.Append(CreateComment($"Call to {call.Method.FullName}"));
                ilProcessor.Emit(OpCodes.Call, SafeImportMethod(call.Method));
            }
            else
            {
                ilProcessor.Append(CreateComment("Invalid call operation"));
                ilProcessor.Emit(OpCodes.Ldstr, "Invalid call operation");
                ilProcessor.Emit(OpCodes.Newobj, SafeImportConstructor(
                    typeof(NotImplementedException),
                    new[] { typeof(string) }));
                ilProcessor.Emit(OpCodes.Throw);
            }

            if (call.Destination != null)
            {
                StoreOperand(call.Destination);
            }
        }

        private MethodReference SafeImportMethod(MethodReference methodRef)
        {
            if (methodRef == null) return null;

            try
            {
                return module.ImportReference(methodRef);
            }
            catch
            {
                return new MethodReference(
                    $"ImportError_{methodRef.Name}",
                    module.TypeSystem.Void,
                    module.TypeSystem.Object);
            }
        }

        private MethodReference SafeImportConstructor(Type type, Type[] parameters)
        {
            try
            {
                var constructor = type.GetConstructor(parameters);
                return module.ImportReference(constructor);
            }
            catch
            {
                return module.ImportReference(typeof(Exception).GetConstructor(new[] { typeof(string) }));
            }
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
            if (labels.TryGetValue(jump.TargetLabel, out var targetLabel))
            {
                ilProcessor.Emit(OpCodes.Br, targetLabel);
            }
            else
            {
                ilProcessor.Emit(OpCodes.Ldstr, $"Missing label: {jump.TargetLabel}");
                ilProcessor.Emit(OpCodes.Throw);
            }
        }

        private void GenerateConditionalJump(ConditionalJumpOperation condJump)
        {
            LoadOperand(condJump.Condition);

            if (!labels.TryGetValue(condJump.TargetLabel, out var targetLabel))
            {
                ilProcessor.Emit(OpCodes.Ldstr, $"Missing label: {condJump.TargetLabel}");
                ilProcessor.Emit(OpCodes.Throw);
                return;
            }

            switch (condJump.Code)
            {
                case ConditionCode.Equal:
                    ilProcessor.Emit(OpCodes.Brtrue, targetLabel);
                    break;
                case ConditionCode.NotEqual:
                    ilProcessor.Emit(OpCodes.Brfalse, targetLabel);
                    break;
                case ConditionCode.Greater:
                    ilProcessor.Emit(OpCodes.Bgt, targetLabel);
                    break;
                case ConditionCode.GreaterOrEqual:
                    ilProcessor.Emit(OpCodes.Bge, targetLabel);
                    break;
                case ConditionCode.Less:
                    ilProcessor.Emit(OpCodes.Blt, targetLabel);
                    break;
                case ConditionCode.LessOrEqual:
                    ilProcessor.Emit(OpCodes.Ble, targetLabel);
                    break;
                case ConditionCode.Above:
                    ilProcessor.Emit(OpCodes.Bgt_Un, targetLabel);
                    break;
                case ConditionCode.Below:
                    ilProcessor.Emit(OpCodes.Blt_Un, targetLabel);
                    break;
                case ConditionCode.Overflow:
                    ilProcessor.Emit(OpCodes.Box, method.Module.TypeSystem.Int32);
                    ilProcessor.Emit(OpCodes.Ldc_I4, 0x800);
                    ilProcessor.Emit(OpCodes.And);
                    ilProcessor.Emit(OpCodes.Brtrue, targetLabel);
                    break;
                case ConditionCode.NoOverflow:
                    ilProcessor.Emit(OpCodes.Box, method.Module.TypeSystem.Int32);
                    ilProcessor.Emit(OpCodes.Ldc_I4, 0x800);
                    ilProcessor.Emit(OpCodes.And);
                    ilProcessor.Emit(OpCodes.Brfalse, targetLabel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(condJump.Code),
                        $"Unsupported condition code: {condJump.Code}");
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

        private void GenerateError(ErrorOperation error)
        {
            ilProcessor.Append(CreateComment($"IR Error: {error.Message}"));
            ilProcessor.Emit(OpCodes.Ldstr, $"IR Error: {error.Message}");
            ilProcessor.Emit(OpCodes.Newobj, SafeImportConstructor(
                typeof(Exception),
                new[] { typeof(string) }));
            ilProcessor.Emit(OpCodes.Throw);
        }

        private Instruction CreateComment(string text)
        {
            // В реальной реализации здесь можно добавить sequence point
            var comment = Instruction.Create(OpCodes.Nop);
            return comment;
        }

        private void ApplyLabels()
        {
            foreach (var label in labels)
            {
                ilProcessor.Body.Instructions.Insert(0, label.Value);
            }
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
            ilProcessor.Emit(OpCodes.Ldobj, SafeImportType(mem.Type));
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
            ilProcessor.Emit(OpCodes.Stobj, SafeImportType(mem.Type));
        }

        private TypeReference SafeImportType(TypeReference typeRef)
        {
            if (typeRef == null) return module.TypeSystem.Object;

            try
            {
                return module.ImportReference(typeRef);
            }
            catch
            {
                return module.TypeSystem.Object;
            }
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
                default:
                    ilProcessor.Emit(OpCodes.Ldstr, value.ToString());
                    break;
            }
        }

        private MethodReference ResolveMethod(ulong va)
        {
            try
            {
                var methodDef = generator.ResolveMethodByVA(va);
                return methodDef != null
                    ? module.ImportReference(methodDef)
                    : CreateStubMethod(va);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResolveMethod] Error resolving method at VA 0x{va:X}: {ex}");
                return CreateStubMethod(va);
            }
        }

        private MethodReference CreateStubMethod(ulong va)
        {
            return new MethodReference(
                $"Unresolved_0x{va:X}",
                module.TypeSystem.Void,
                module.ImportReference(typeof(object)))
            {
                HasThis = false
            };
        }

        private VariableDefinition GetRegister(string name, TypeReference type)
        {
            if (!registers.TryGetValue(name, out var variable))
            {
                variable = new VariableDefinition(SafeImportType(type));
                method.Body.Variables.Add(variable);
                registers[name] = variable;
            }
            return variable;
        }

        private void AddReturnIfMissing()
        {
            if (ilProcessor.Body.Instructions.Count == 0 ||
                ilProcessor.Body.Instructions[^1].OpCode != OpCodes.Ret)
            {
                ilProcessor.Emit(OpCodes.Ret);
            }
        }
    }
}