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
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _initialTypes = CreateInitialTypes();
        }

        private Dictionary<string, TypeReference> CreateInitialTypes()
        {
            var types = new Dictionary<string, TypeReference>(StringComparer.OrdinalIgnoreCase);

            try
            {
                TypeReference CreateType(Type type)
                {
                    if (_module == null)
                    {
                        Console.WriteLine("[CreateType] Warning: _module is null, creating fallback TypeReference.");
                        return new TypeReference(type.Namespace, type.Name, null, null);
                    }

                    try
                    {
                        return _module.ImportReference(type);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CreateType] Error importing type {type.FullName}: {ex.Message}");
                        return new TypeReference(type.Namespace, type.Name, _module, null);
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
            catch (Exception ex)
            {
                Console.WriteLine($"[CreateInitialTypes] Error initializing types: {ex.Message}");
            }

            return types;
        }

        public Dictionary<string, TypeReference> AnalyzeTypes(List<IROperation> operations)
        {
            if (operations == null)
            {
                Console.WriteLine("[AnalyzeTypes] Error: operations list is null.");
                return new Dictionary<string, TypeReference>(_initialTypes);
            }

            var typeMap = new Dictionary<string, TypeReference>(_initialTypes);
            bool changed;
            var pass = 0;
            const int maxPasses = 10;

            do
            {
                changed = false;
                foreach (var op in operations)
                {
                    if (op == null) continue;

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
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AnalyzeTypes] Error processing operation: {ex.Message}");
                    }
                }
                pass++;
            } while (changed && pass < maxPasses);

            return typeMap;
        }

        private bool ProcessAssign(AssignOperation assign, Dictionary<string, TypeReference> typeMap)
        {
            if (assign == null || assign.Destination is not RegisterOperand destReg || string.IsNullOrEmpty(destReg.Name))
                return false;

            var sourceType = InferType(assign.Source, typeMap);
            if (sourceType == null)
                return false;

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) || !DataFlowAnalyzer.AreTypesCompatible(currentType, sourceType))
            {
                typeMap[destReg.Name] = sourceType;
                return true;
            }
            return false;
        }

        private bool ProcessBinary(BinaryOperation binary, Dictionary<string, TypeReference> typeMap)
        {
            if (binary == null || binary.Destination is not RegisterOperand destReg || string.IsNullOrEmpty(destReg.Name))
                return false;

            var resultType = InferBinaryResultType(binary.OperationType, binary.Left, binary.Right, typeMap);
            if (resultType == null)
                return false;

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) || !DataFlowAnalyzer.AreTypesCompatible(currentType, resultType))
            {
                typeMap[destReg.Name] = resultType;
                return true;
            }
            return false;
        }

        private bool ProcessCall(CallOperation call, Dictionary<string, TypeReference> typeMap)
        {
            if (call == null || call.Destination is not RegisterOperand destReg || string.IsNullOrEmpty(destReg.Name))
                return false;

            var returnType = call.Method?.ReturnType ?? SafeGetObjectType();
            if (returnType == null)
            {
                Console.WriteLine("[ProcessCall] Error: returnType is null.");
                return false;
            }

            if (!typeMap.TryGetValue(destReg.Name, out var currentType) || !DataFlowAnalyzer.AreTypesCompatible(currentType, returnType))
            {
                typeMap[destReg.Name] = returnType;
                return true;
            }
            return false;
        }

        private TypeReference InferType(IROperand operand, Dictionary<string, TypeReference> typeMap)
        {
            if (operand == null)
            {
                Console.WriteLine("[InferType] Warning: operand is null, returning Object type.");
                return SafeGetObjectType();
            }

            try
            {
                return operand switch
                {
                    RegisterOperand reg => GetRegisterType(reg, typeMap),
                    ConstantOperand con => con.Type ?? GetTypeForConstant(con.Value),
                    MemoryOperand mem => mem.Type ?? SafeGetObjectType(),
                    _ => SafeGetObjectType()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InferType] Error inferring type: {ex.Message}");
                return SafeGetObjectType();
            }
        }

        private TypeReference GetRegisterType(RegisterOperand reg, Dictionary<string, TypeReference> typeMap)
        {
            if (reg == null || string.IsNullOrEmpty(reg.Name))
            {
                Console.WriteLine("[GetRegisterType] Warning: reg or reg.Name is null.");
                return SafeGetObjectType();
            }

            try
            {
                if (typeMap != null && typeMap.TryGetValue(reg.Name, out var currentType))
                    return currentType;

                if (_initialTypes != null && _initialTypes.TryGetValue(reg.Name, out var initType))
                    return initType;

                return reg.Name.ToLower() switch
                {
                    "eax" or "ebx" or "ecx" or "edx" or "esi" or "edi" or "ebp" or "esp"
                        => CreateFallbackType("System", "Int32"),
                    "rax" or "rbx" or "rcx" or "rdx" or "rsi" or "rdi" or "rbp" or "rsp"
                        => CreateFallbackType("System", "Int64"),
                    "eflags" => CreateFallbackType("System", "UInt32"),
                    _ => SafeGetObjectType()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetRegisterType] Error getting register type: {ex.Message}");
                return SafeGetObjectType();
            }
        }

        private TypeReference SafeGetObjectType()
        {
            try
            {
                if (_module == null)
                {
                    Console.WriteLine("[SafeGetObjectType] Error: _module is null.");
                    return new TypeReference("System", "Object", null, null);
                }

                if (_module.TypeSystem == null)
                {
                    Console.WriteLine("[SafeGetObjectType] Error: _module.TypeSystem is null.");
                    return new TypeReference("System", "Object", _module, null);
                }

                if (_module.TypeSystem.Object == null)
                {
                    Console.WriteLine("[SafeGetObjectType] Error: _module.TypeSystem.Object is null.");
                    var fallbackRef = new TypeReference("System", "Object", _module, null);
                    return fallbackRef;
                }

                return _module.TypeSystem.Object;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SafeGetObjectType] Exception occurred: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return new TypeReference("System", "Object", _module, null);
            }
        }

        private TypeReference CreateFallbackType(string @namespace, string name)
        {
            try
            {
                return new TypeReference(@namespace, name, _module, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CreateFallbackType] Error creating fallback type: {ex.Message}");
                return new TypeReference("System", "Object", _module, null);
            }
        }

        private TypeReference GetTypeForConstant(object value)
        {
            if (value == null)
            {
                Console.WriteLine("[GetTypeForConstant] Warning: value is null.");
                return SafeGetObjectType();
            }

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
            catch (Exception ex)
            {
                Console.WriteLine($"[GetTypeForConstant] Error getting constant type: {ex.Message}");
                return SafeGetObjectType();
            }
        }

        private TypeReference InferBinaryResultType(BinaryOperationType opType, IROperand left, IROperand right, Dictionary<string, TypeReference> typeMap)
        {
            var leftType = InferType(left, typeMap);
            var rightType = InferType(right, typeMap);

            if (leftType == null || rightType == null)
            {
                Console.WriteLine("[InferBinaryResultType] Warning: leftType or rightType is null.");
                return SafeGetObjectType();
            }

            return opType switch
            {
                BinaryOperationType.Add or BinaryOperationType.Sub or BinaryOperationType.Mul or
                BinaryOperationType.Div or BinaryOperationType.Rem => NumericResultType(leftType, rightType),
                BinaryOperationType.And or BinaryOperationType.Or or BinaryOperationType.Xor => LogicalResultType(leftType, rightType),
                BinaryOperationType.Equal or BinaryOperationType.NotEqual or BinaryOperationType.GreaterThan or
                BinaryOperationType.LessThan => _module?.TypeSystem?.Boolean ?? CreateFallbackType("System", "Boolean"),
                _ => SafeGetObjectType()
            };
        }

        private TypeReference NumericResultType(TypeReference left, TypeReference right)
        {
            if (left == null || right == null)
            {
                Console.WriteLine("[NumericResultType] Warning: left or right type is null.");
                return SafeGetObjectType();
            }

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
                var fullName = type?.FullName;
                return fullName != null && numericTypes.TryGetValue(fullName, out var priority) ? priority : -1;
            }

            var leftPriority = GetPriority(left);
            var rightPriority = GetPriority(right);

            if (leftPriority < 0) return right;
            if (rightPriority < 0) return left;

            return leftPriority > rightPriority ? left : right;
        }

        private TypeReference LogicalResultType(TypeReference left, TypeReference right)
        {
            if (left == null || right == null)
            {
                Console.WriteLine("[LogicalResultType] Warning: left or right type is null.");
                return SafeGetObjectType();
            }

            if (IsIntegerType(left) && IsIntegerType(right))
                return NumericResultType(left, right);

            return _module?.TypeSystem?.Int32 ?? CreateFallbackType("System", "Int32");
        }

        private bool IsIntegerType(TypeReference type)
        {
            if (type == null)
            {
                Console.WriteLine("[IsIntegerType] Warning: type is null.");
                return false;
            }

            var fullName = type.FullName;
            return fullName switch
            {
                "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or
                "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => true,
                _ => false
            };
        }

        private static bool AreTypesCompatible(TypeReference type1, TypeReference type2)
        {
            if (type1 == null || type2 == null)
            {
                Console.WriteLine("[AreTypesCompatible] Warning: type1 or type2 is null.");
                return false;
            }
            return type1.FullName == type2.FullName;
        }
    }
}