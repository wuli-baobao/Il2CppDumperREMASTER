using Il2CppDumper.lifting.Operation;
using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace Il2CppDumper.lifting
{
    internal class DataFlowAnalyzer
    {
        private readonly ModuleDefinition _module;
        private readonly Dictionary<string, TypeReference> _initialTypes;

        public DataFlowAnalyzer(ModuleDefinition module)
        {
            _module = module;
            _initialTypes = CreateInitialTypes();
        }
        private Dictionary<string, TypeReference> CreateInitialTypes()
        {
            var types = new Dictionary<string, TypeReference>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Безопасное создание предопределенных типов
                TypeReference CreateType(Type type)
                {
                    if (_module == null)
                    {
                        return new TypeReference(type.Namespace, type.Name, null, null);
                    }

                    try
                    {
                        return _module.ImportReference(type);
                    }
                    catch
                    {
                        return new TypeReference(type.Namespace, type.Name, null, null);
                    }
                }

                types["eax"] = CreateType(typeof(int));
                types["ebx"] = CreateType(typeof(int));
                types["ecx"] = CreateType(typeof(int));
                types["edx"] = CreateType(typeof(int));
                types["esi"] = CreateType(typeof(int));
                types["edi"] = CreateType(typeof(int));
                types["ebp"] = CreateType(typeof(int));
                types["esp"] = CreateType(typeof(int));

                types["rax"] = CreateType(typeof(long));
                types["rbx"] = CreateType(typeof(long));
                types["rcx"] = CreateType(typeof(long));
                types["rdx"] = CreateType(typeof(long));
                types["rsi"] = CreateType(typeof(long));
                types["rdi"] = CreateType(typeof(long));
                types["rbp"] = CreateType(typeof(long));
                types["rsp"] = CreateType(typeof(long));

                types["eflags"] = CreateType(typeof(uint));
            }
            catch
            {
                // Игнорируем ошибки инициализации
            }

            return types;
        }

        public Dictionary<string, TypeReference> AnalyzeTypes(List<IROperation> operations)
        {
            var typeMap = new Dictionary<string, TypeReference>(_initialTypes);
            bool changed;
            var pass = 0;
            const int maxPasses = 10;

            do
            {
                changed = false;
                foreach (var op in operations)
                {
                    try
                    {
                        switch (op)
                        {
                            case AssignOperation assign:
                                changed |= ProcessAssign(assign, typeMap);
                                break;

                            case BinaryOperation binary:
                                changed |= ProcessBinary(binary, typeMap);
                                break;

                            case CallOperation call:
                                changed |= ProcessCall(call, typeMap);
                                break;
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки при обработке отдельных операций
                    }
                }
                pass++;
            } while (changed && pass < maxPasses);

            return typeMap;
        }

        private bool ProcessAssign(AssignOperation assign, Dictionary<string, TypeReference> typeMap)
        {
            if (assign.Destination is not RegisterOperand destReg)
                return false;

            var sourceType = InferType(assign.Source, typeMap);
            if (sourceType == null)
                return false;

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) ||
                !AreTypesCompatible(currentType, sourceType))
            {
                typeMap[destReg.Name] = sourceType;
                return true;
            }
            return false;
        }

        private bool ProcessBinary(BinaryOperation binary, Dictionary<string, TypeReference> typeMap)
        {
            if (binary.Destination is not RegisterOperand destReg)
                return false;

            var resultType = InferBinaryResultType(
                binary.OperationType,
                binary.Left,
                binary.Right,
                typeMap);

            if (resultType == null)
                return false;

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) ||
                !AreTypesCompatible(currentType, resultType))
            {
                typeMap[destReg.Name] = resultType;
                return true;
            }
            return false;
        }

        private bool ProcessCall(CallOperation call, Dictionary<string, TypeReference> typeMap)
        {
            if (call.Destination is not RegisterOperand destReg)
                return false;

            var returnType = call.Method?.ReturnType ?? SafeGetObjectType();

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) ||
                !AreTypesCompatible(currentType, returnType))
            {
                typeMap[destReg.Name] = returnType;
                return true;
            }
            return false;
        }

        private TypeReference InferType(IROperand operand, Dictionary<string, TypeReference> typeMap)
        {
            if (operand == null)
                return SafeGetObjectType();

            try
            {
                return operand switch
                {
                    RegisterOperand reg =>
                        GetRegisterType(reg, typeMap),

                    ConstantOperand con =>
                        con.Type ?? GetTypeForConstant(con.Value),

                    MemoryOperand mem =>
                        mem.Type ?? SafeGetObjectType(),

                    _ => SafeGetObjectType()
                };
            }
            catch
            {
                return SafeGetObjectType();
            }
        }

        private TypeReference GetRegisterType(RegisterOperand reg, Dictionary<string, TypeReference> typeMap)
        {
            if (string.IsNullOrEmpty(reg?.Name))
                return SafeGetObjectType();

            try
            {
                // 1. Проверяем текущий тип в typeMap
                if (typeMap != null && typeMap.TryGetValue(reg.Name, out var currentType))
                    return currentType;

                // 2. Проверяем предопределенные типы
                if (_initialTypes != null && _initialTypes.TryGetValue(reg.Name, out var initType))
                    return initType;

                // 3. Определяем тип по имени регистра
                return reg.Name.ToLower() switch
                {
                    "eax" or "ebx" or "ecx" or "edx" or "esi" or "edi" or "ebp" or "esp"
                        => CreateFallbackType("System", "Int32"),

                    "rax" or "rbx" or "rcx" or "rdx" or "rsi" or "rdi" or "rbp" or "rsp"
                        => CreateFallbackType("System", "Int64"),

                    "eflags"
                        => CreateFallbackType("System", "UInt32"),

                    _ => SafeGetObjectType()
                };
            }
            catch
            {
                return SafeGetObjectType();
            }
        }

        private TypeReference SafeGetObjectType()
        {
            try
            {
                // 1. Проверяем, что _module и TypeSystem не null
                if (_module != null && _module.TypeSystem != null && _module.TypeSystem.Object != null)
                {
                    Console.WriteLine(_module.TypeSystem.Object.ToString());
                    return _module.TypeSystem.Object;
                }
                // 2. Создаем TypeReference безопасно
                return new TypeReference("System", "Object", null, null);
            }
            catch
            {
                // 3. Используем резервный вариант
                return new TypeReference("System", "Object", null, null);
            }
        }

        private TypeReference CreateFallbackType(string @namespace, string name)
        {
            try
            {
                // Всегда создаем новый TypeReference вместо импорта
                return new TypeReference(@namespace, name, null, null);
            }
            catch
            {
                // Возвращаем тип Object при ошибке
                return SafeGetObjectType();
            }
        }

        private TypeReference GetTypeForConstant(object value)
        {
            if (value == null)
                return SafeGetObjectType();

            try
            {
                return value switch
                {
                    int _ => CreateFallbackType("System", "Int32"),
                    uint _ => CreateFallbackType("System", "UInt32"),
                    long _ => CreateFallbackType("System", "Int64"),
                    ulong _ => CreateFallbackType("System", "UInt64"),
                    float _ => CreateFallbackType("System", "Single"),
                    double _ => CreateFallbackType("System", "Double"),
                    bool _ => CreateFallbackType("System", "Boolean"),
                    string _ => CreateFallbackType("System", "String"),
                    _ => SafeGetObjectType()
                };
            }
            catch
            {
                return SafeGetObjectType();
            }
        }

        private TypeReference InferBinaryResultType(
            BinaryOperationType opType,
            IROperand left,
            IROperand right,
            Dictionary<string, TypeReference> typeMap)
        {
            var leftType = InferType(left, typeMap);
            var rightType = InferType(right, typeMap);

            return opType switch
            {
                BinaryOperationType.Add or
                BinaryOperationType.Sub or
                BinaryOperationType.Mul or
                BinaryOperationType.Div or
                BinaryOperationType.Rem
                    => NumericResultType(leftType, rightType),

                BinaryOperationType.And or
                BinaryOperationType.Or or
                BinaryOperationType.Xor
                    => LogicalResultType(leftType, rightType),

                BinaryOperationType.Equal or
                BinaryOperationType.NotEqual or
                BinaryOperationType.GreaterThan or
                BinaryOperationType.LessThan
                    => _module?.TypeSystem?.Boolean ?? CreateFallbackType("System", "Boolean"),

                _ => SafeGetObjectType()
            };
        }

        private TypeReference NumericResultType(TypeReference left, TypeReference right)
        {
            if (left == null || right == null)
                return SafeGetObjectType();

            var numericTypes = new Dictionary<string, int>
            {
                ["System.Byte"] = 0,
                ["System.SByte"] = 1,
                ["System.Int16"] = 2,
                ["System.UInt16"] = 3,
                ["System.Int32"] = 4,
                ["System.UInt32"] = 5,
                ["System.Int64"] = 6,
                ["System.UInt64"] = 7,
                ["System.Single"] = 8,
                ["System.Double"] = 9
            };

            int GetPriority(TypeReference type)
            {
                var fullName = type.FullName;
                return numericTypes.TryGetValue(fullName, out var priority)
                    ? priority
                    : -1;
            }

            var leftPriority = GetPriority(left);
            var rightPriority = GetPriority(right);

            if (leftPriority < 0) return right;
            if (rightPriority < 0) return left;

            return leftPriority > rightPriority ? left : right;
        }

        private TypeReference LogicalResultType(TypeReference left, TypeReference right)
        {
            if (IsIntegerType(left) && IsIntegerType(right))
                return NumericResultType(left, right);

            return _module?.TypeSystem?.Int32 ?? CreateFallbackType("System", "Int32");
        }

        private bool IsIntegerType(TypeReference type)
        {
            if (type == null) return false;

            var fullName = type.FullName;
            return fullName switch
            {
                "System.Byte" or "System.SByte" or
                "System.Int16" or "System.UInt16" or
                "System.Int32" or "System.UInt32" or
                "System.Int64" or "System.UInt64" => true,
                _ => false
            };
        }

        private bool AreTypesCompatible(TypeReference type1, TypeReference type2)
        {
            if (type1 == null || type2 == null) return false;
            return type1.FullName == type2.FullName;
        }
    }
}