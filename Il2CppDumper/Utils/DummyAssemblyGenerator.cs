using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
// Используем пространства имен из Gee.External.Capstone
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Gee.External.Capstone.X86;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public class DummyAssemblyGenerator
    {
        public List<AssemblyDefinition> Assemblies = new();

        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;
        private readonly Dictionary<Il2CppTypeDefinition, TypeDefinition> typeDefinitionDic = new();
        private readonly Dictionary<Il2CppGenericParameter, GenericParameter> genericParameterDic = new();
        private readonly MethodDefinition attributeAttribute;
        private readonly TypeReference stringType;
        private readonly TypeSystem typeSystem;
        private readonly Dictionary<int, FieldDefinition> fieldDefinitionDic = new();
        private readonly Dictionary<int, PropertyDefinition> propertyDefinitionDic = new();
        private readonly Dictionary<int, MethodDefinition> methodDefinitionDic = new();

        public DummyAssemblyGenerator(Il2CppExecutor il2CppExecutor, bool addToken)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;

            //Il2CppDummyDll
            var il2CppDummyDll = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource1.Il2CppDummyDll));
            Assemblies.Add(il2CppDummyDll);
            var dummyMD = il2CppDummyDll.MainModule;
            var addressAttribute = dummyMD.Types.First(x => x.Name == "AddressAttribute").Methods[0];
            var fieldOffsetAttribute = dummyMD.Types.First(x => x.Name == "FieldOffsetAttribute").Methods[0];
            attributeAttribute = dummyMD.Types.First(x => x.Name == "AttributeAttribute").Methods[0];
            var metadataOffsetAttribute = dummyMD.Types.First(x => x.Name == "MetadataOffsetAttribute").Methods[0];
            var tokenAttribute = dummyMD.Types.First(x => x.Name == "TokenAttribute").Methods[0];
            stringType = dummyMD.TypeSystem.String;
            typeSystem = dummyMD.TypeSystem;

            var resolver = new MyAssemblyResolver();
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            resolver.Register(il2CppDummyDll);

            var parameterDefinitionDic = new Dictionary<int, ParameterDefinition>();
            var eventDefinitionDic = new Dictionary<int, EventDefinition>();

            //创建程序集，同时创建所有类
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var aname = metadata.assemblyDefs[imageDef.assemblyIndex].aname;
                var assemblyName = metadata.GetStringFromIndex(aname.nameIndex);
                Version vers;
                if (aname.build >= 0)
                {
                    vers = new Version(aname.major, aname.minor, aname.build, aname.revision);
                }
                else
                {
                    //__Generated
                    vers = new Version(3, 7, 1, 6);
                }
                var assemblyNameDef = new AssemblyNameDefinition(assemblyName, vers);
                /*assemblyNameDef.Culture = metadata.GetStringFromIndex(aname.cultureIndex);
                assemblyNameDef.PublicKey = Encoding.UTF8.GetBytes(metadata.GetStringFromIndex(aname.publicKeyIndex));
                assemblyNameDef.HashAlgorithm = (AssemblyHashAlgorithm)aname.hash_alg;
                assemblyNameDef.Attributes = (AssemblyAttributes)aname.flags;
                assemblyNameDef.PublicKeyToken = aname.public_key_token;*/
                var assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyNameDef, imageName, moduleParameters);
                resolver.Register(assemblyDefinition);
                Assemblies.Add(assemblyDefinition);
                var moduleDefinition = assemblyDefinition.MainModule;
                moduleDefinition.Types.Clear();//清除自动创建的<Module>类
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var namespaceName = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                    var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
                    var typeDefinition = new TypeDefinition(namespaceName, typeName, (TypeAttributes)typeDef.flags);
                    typeDefinitionDic.Add(typeDef, typeDefinition);
                    if (typeDef.declaringTypeIndex == -1)
                    {
                        moduleDefinition.Types.Add(typeDefinition);
                    }
                }
            }
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //nestedtype
                    for (int i = 0; i < typeDef.nested_type_count; i++)
                    {
                        var nestedIndex = metadata.nestedTypeIndices[typeDef.nestedTypesStart + i];
                        var nestedTypeDef = metadata.typeDefs[nestedIndex];
                        var nestedTypeDefinition = typeDefinitionDic[nestedTypeDef];
                        typeDefinition.NestedTypes.Add(nestedTypeDefinition);
                    }
                }
            }
            //提前处理
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    if (addToken)
                    {
                        var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                        customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{typeDef.token:X}")));
                        typeDefinition.CustomAttributes.Add(customTokenAttribute);
                    }

                    //genericParameter
                    if (typeDef.genericContainerIndex >= 0)
                    {
                        var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                        for (int i = 0; i < genericContainer.type_argc; i++)
                        {
                            var genericParameterIndex = genericContainer.genericParameterStart + i;
                            var param = metadata.genericParameters[genericParameterIndex];
                            var genericParameter = CreateGenericParameter(param, typeDefinition);
                            typeDefinition.GenericParameters.Add(genericParameter);
                        }
                    }

                    //parent
                    if (typeDef.parentIndex >= 0)
                    {
                        var parentType = il2Cpp.types[typeDef.parentIndex];
                        var parentTypeRef = GetTypeReference(typeDefinition, parentType);
                        typeDefinition.BaseType = parentTypeRef;
                    }

                    //interfaces
                    for (int i = 0; i < typeDef.interfaces_count; i++)
                    {
                        var interfaceType = il2Cpp.types[metadata.interfaceIndices[typeDef.interfacesStart + i]];
                        var interfaceTypeRef = GetTypeReference(typeDefinition, interfaceType);
                        typeDefinition.Interfaces.Add(new InterfaceImplementation(interfaceTypeRef));
                    }
                }
            }
            //处理field, method, property等等
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (int index = imageDef.typeStart; index < typeEnd; index++)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //field
                    var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                    for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                    {
                        var fieldDef = metadata.fieldDefs[i];
                        var fieldType = il2Cpp.types[fieldDef.typeIndex];
                        var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                        var fieldTypeRef = GetTypeReference(typeDefinition, fieldType);
                        var fieldDefinition = new FieldDefinition(fieldName, (FieldAttributes)fieldType.attrs, fieldTypeRef);
                        typeDefinition.Fields.Add(fieldDefinition);
                        fieldDefinitionDic.Add(i, fieldDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{fieldDef.token:X}")));
                            fieldDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        //fieldDefault
                        if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefault) && fieldDefault.dataIndex != -1)
                        {
                            if (executor.TryGetDefaultValue(fieldDefault.typeIndex, fieldDefault.dataIndex, out var value))
                            {
                                fieldDefinition.Constant = value;
                            }
                            else
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(metadataOffsetAttribute));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{value:X}"));
                                customAttribute.Fields.Add(offset);
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                        //fieldOffset
                        if (!fieldDefinition.IsLiteral)
                        {
                            var fieldOffset = il2Cpp.GetFieldOffsetFromIndex(index, i - typeDef.fieldStart, i, typeDefinition.IsValueType, fieldDefinition.IsStatic);
                            if (fieldOffset >= 0)
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(fieldOffsetAttribute));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fieldOffset:X}"));
                                customAttribute.Fields.Add(offset);
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //method
                    var methodEnd = typeDef.methodStart + typeDef.method_count;
                    for (var i = typeDef.methodStart; i < methodEnd; ++i)
                    {
                        var methodDef = metadata.methodDefs[i];
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodDefinition = new MethodDefinition(methodName, (MethodAttributes)methodDef.flags, typeDefinition.Module.ImportReference(typeSystem.Void))
                        {
                            ImplAttributes = (MethodImplAttributes)methodDef.iflags
                        };
                        typeDefinition.Methods.Add(methodDefinition);
                        //genericParameter
                        if (methodDef.genericContainerIndex >= 0)
                        {
                            var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                            for (int j = 0; j < genericContainer.type_argc; j++)
                            {
                                var genericParameterIndex = genericContainer.genericParameterStart + j;
                                var param = metadata.genericParameters[genericParameterIndex];
                                var genericParameter = CreateGenericParameter(param, methodDefinition);
                                methodDefinition.GenericParameters.Add(genericParameter);
                            }
                        }
                        var methodReturnType = il2Cpp.types[methodDef.returnType];
                        var returnType = GetTypeReferenceWithByRef(methodDefinition, methodReturnType);
                        methodDefinition.ReturnType = returnType;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                            methodDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        if (methodDefinition.HasBody && typeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                        {
                            var ilprocessor = methodDefinition.Body.GetILProcessor();
                            var bodyChanged = false;

                            // Аннотации
                            var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                            var methodRVA = methodPointer > 0 ? il2Cpp.GetRVA(methodPointer) : 0;
                            var methodVA = methodPointer > 0 ? il2Cpp.MapVATR(methodPointer) : 0; // Используем MapVATR для получения VA из файла

                            var methodFullNameBuilder = new System.Text.StringBuilder();
                            methodFullNameBuilder.Append(typeDefinition.FullName).Append(".").Append(methodName);
                            methodFullNameBuilder.Append("(");
                            for(int pIdx = 0; pIdx < methodDef.parameterCount; ++pIdx)
                            {
                                var paramDef = metadata.parameterDefs[methodDef.parameterStart + pIdx];
                                var paramType = il2Cpp.types[paramDef.typeIndex];
                                methodFullNameBuilder.Append(executor.GetTypeName(paramType, true, true)); // Используем полное имя для типов параметров
                                if (pIdx < methodDef.parameterCount - 1)
                                    methodFullNameBuilder.Append(", ");
                            }
                            methodFullNameBuilder.Append(")");

                            List<string> comments = new List<string>
                            {
                                $"Method: {methodFullNameBuilder.ToString()}",
                                $"Method Token: 0x{methodDef.token:X}",
                                $"Method Address RVA: 0x{methodRVA:X}, VA: 0x{methodVA:X}", // VA может быть неточным если базовый адрес не тот
                            };

                            // Гипотетический вызов для получения информации о вызываемых методах
                            // List<string> calledMethods = GetCalledMethodsInfo(methodDef, imageName, methodPointer);
                            // if (calledMethods.Any())
                            // {
                            //    comments.Add("Calls:");
                            //    foreach (var calledMethod in calledMethods)
                            //    {
                            //        comments.Add($"  - {calledMethod}");
                            //    }
                            // }
                            // Пока оставим заглушку, так как GetCalledMethodsInfo не реализован
                            // comments.Add("Calls: (Analysis not yet implemented)"); // Будет заменено ниже

                            // --- Начало кода для дизассемблирования ---
                            byte[] codeBytes = null;
                            uint readMethodSize = 0; // Размер для чтения
                            ArchitectureType architecture = this.il2Cpp.GetArchitectureType();

                            if (methodPointer > 0 && architecture != ArchitectureType.Unknown)
                            {
                                var sortedRVAs = this.executor.GetSortedFunctionRVAs();
                                if (Il2CppExecutor.TryGetMethodSize(methodRVA, sortedRVAs, out var determinedSize) && determinedSize > 0)
                                {
                                    readMethodSize = determinedSize;
                                }
                                else
                                {
                                    // Если размер не определен (например, последний метод или ошибка),
                                    // можно попробовать прочитать фиксированный блок или ничего не делать.
                                    // Для начала, если размер не определен точно, не будем дизассемблировать.
                                    // Позже можно установить readMethodSize = DEFAULT_DISASM_SIZE (e.g. 256 bytes)
                                }

                                if (readMethodSize > 0)
                                {
                                    const uint maxReasonableSize = 8192; // Увеличим до 8KB
                                    if (readMethodSize > maxReasonableSize)
                                    {
                                        //Console.WriteLine($"[DummyAssemblyGenerator] Method size {readMethodSize} for {methodFullNameBuilder} at RVA 0x{methodRVA:X} is too large, capping at {maxReasonableSize}.");
                                        readMethodSize = maxReasonableSize;
                                    }

                                    try
                                    {
                                        ulong fileOffset = this.il2Cpp.MapVATR(methodPointer);
                                        if (fileOffset > 0 || (methodPointer == 0 && fileOffset == 0) ) // fileOffset может быть 0 для VA=0
                                        {
                                            // Добавим проверку, что fileOffset + readMethodSize не выходит за пределы файла, если это возможно
                                            // long fileSize = this.il2Cpp.Length;
                                            // if (fileOffset + readMethodSize > (ulong)fileSize) {
                                            //     readMethodSize = (uint)(fileSize - (long)fileOffset);
                                            //     if (readMethodSize <=0) codeBytes = Array.Empty<byte>();
                                            // }

                                            if (readMethodSize > 0)
                                            {
                                                lock(this.il2Cpp)
                                                {
                                                    this.il2Cpp.Position = fileOffset;
                                                    codeBytes = this.il2Cpp.ReadBytes((int)readMethodSize);
                                                }
                                            }
                                            else if (readMethodSize == 0 && fileOffset > 0) // Если размер получился 0, но есть смещение
                                            {
                                                codeBytes = Array.Empty<byte>();
                                            }
                                        }
                                        else if (methodPointer > 0)
                                        {
                                           //Console.WriteLine($"[DummyAssemblyGenerator] Failed to map VA 0x{methodPointer:X} to file offset for method {methodFullNameBuilder}. Skipping disassembly.");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                       //Console.WriteLine($"[DummyAssemblyGenerator] Error reading bytes for method {methodFullNameBuilder} at VA 0x{methodPointer:X}: {ex.Message}");
                                        codeBytes = null;
                                    }
                                }
                            }

                            List<DisassembledInstruction> disassembledInstructions = null;
                            if (codeBytes != null && codeBytes.Length > 0 && architecture != ArchitectureType.Unknown)
                            {
                                disassembledInstructions = MethodDisassembler.Disassemble(codeBytes, methodPointer, architecture);
                            }

                            List<string> assemblyListing = new List<string>();
                            List<string> calledMethodsAnalysis = new List<string>(); // Для анализа вызовов
                            const int maxInstructionsToShow = 25;

                            if (disassembledInstructions != null && disassembledInstructions.Any())
                            {
                                assemblyListing.Add("Assembly Listing (up to " + maxInstructionsToShow + " instructions or first return):");
                                bool retFound = false;
                                for (int k = 0; k < disassembledInstructions.Count && k < maxInstructionsToShow && !retFound; ++k)
                                {
                                    var instr = disassembledInstructions[k];
                                    assemblyListing.Add($"  {instr.ToString()}");
                                    if (instr.Mnemonic.StartsWith("ret", StringComparison.OrdinalIgnoreCase))
                                    {
                                        retFound = true;
                                    }

                                    // Анализ вызовов с использованием Gee.External.Capstone
                                    bool isCallInstruction = instr.Mnemonic.Equals("call", StringComparison.OrdinalIgnoreCase) ||
                                                             instr.Mnemonic.Equals("bl", StringComparison.OrdinalIgnoreCase) ||
                                                             instr.Mnemonic.Equals("blx", StringComparison.OrdinalIgnoreCase);

                                    if (isCallInstruction && instr.PlatformSpecificInstruction != null)
                                    {
                                        ulong targetVa = 0;
                                        string callTypeInfo = "";

                                        if (instr.PlatformSpecificInstruction is X86Instruction x86Instr)
                                        {
                                            var x86Details = x86Instr.Details;
                                            if (x86Details != null && x86Details.Operands.Length > 0)
                                            {
                                                var op = x86Details.Operands[0];
                                                if (op.Type == X86OperandType.Immediate)
                                                {
                                                    targetVa = (ulong)op.Value.Immediate;
                                                    callTypeInfo = " (direct imm)";
                                                }
                                                else if (op.Type == X86OperandType.Memory)
                                                {
                                                    if (op.Value.Memory.Base == X86Register.Invalid && // Используем Invalid
                                                        op.Value.Memory.Index == X86Register.Invalid &&
                                                        op.Value.Memory.Displacement != 0)
                                                    {
                                                        callTypeInfo = $" (indirect via mem 0x{op.Value.Memory.Displacement:X})";
                                                    } else {
                                                        callTypeInfo = " (indirect mem)";
                                                    }
                                                }
                                            }
                                        }
                                        else if (instr.PlatformSpecificInstruction is ArmInstruction armInstr)
                                        {
                                            var armDetails = armInstr.Details;
                                            if (armDetails != null && armDetails.Operands.Length > 0)
                                            {
                                                var op = armDetails.Operands[0];
                                                if (op.Type == ArmOperandType.Immediate)
                                                {
                                                    targetVa = (ulong)op.Value.Immediate;
                                                    callTypeInfo = " (branch)";
                                                }
                                            }
                                        }
                                        else if (instr.PlatformSpecificInstruction is Arm64Instruction arm64Instr)
                                        {
                                            var arm64Details = arm64Instr.Details;
                                            if (arm64Details != null && arm64Details.Operands.Length > 0)
                                            {
                                                var op = arm64Details.Operands[0];
                                                if (op.Type == Arm64OperandType.Immediate)
                                                {
                                                    targetVa = (ulong)op.Value.Immediate;
                                                    callTypeInfo = " (branch)";
                                                }
                                            }
                                        }

                                        if (targetVa != 0)
                                        {
                                            calledMethodsAnalysis.Add($"  -> Calls VA: 0x{targetVa:X}{callTypeInfo}");
                                        }
                                        else if (!string.IsNullOrEmpty(callTypeInfo))
                                        {
                                             calledMethodsAnalysis.Add($"  -> {instr.Mnemonic}{callTypeInfo} (target VA unresolved)");
                                        }
                                    }
                                }
                            }
                            else if (methodPointer > 0 && architecture != ArchitectureType.Unknown)
                            {
                                if (readMethodSize == 0 && codeBytes == null)
                                {
                                    assemblyListing.Add("  (Could not determine method size for disassembly)");
                                }
                                else if (codeBytes == null)
                                {
                                     assemblyListing.Add("  (Could not read method bytes for disassembly)");
                                }
                                else
                                {
                                    assemblyListing.Add("  (No instructions disassembled or method is empty)");
                                }
                            }

                            if (assemblyListing.Any())
                            {
                                comments.AddRange(assemblyListing);
                            }

                            // Обновленная заглушка/результат для вызовов
                            if (calledMethodsAnalysis.Any()) // Если бы анализ вызовов был реализован
                            {
                                comments.Add("Identified Calls:");
                                comments.AddRange(calledMethodsAnalysis);
                            }
                            else
                            {
                                comments.Add("Calls: (Call analysis not yet implemented or no calls identified)");
                            }
                            // --- Конец кода для дизассемблирования ---

                            comments.Add("Body not decompiled by Il2CppReanimator.");

                            // Добавляем комментарии как Nop инструкции (для dnSpy и других декомпиляторов)
                            // dnSpy не показывает комментарии к инструкциям напрямую, лучше использовать строковые литералы или исключения.
                            // Вместо Nop, будем формировать сообщение для исключения.

                            string exceptionMessage = string.Join(Environment.NewLine, comments);

                            // Используем methodDefinition.Module для импорта ссылок
                            var systemNotImplementedExceptionRef = methodDefinition.Module.ImportReference(typeof(NotImplementedException));
                            // Для Resolve() лучше импортировать конструктор напрямую, если известен его тип
                            var notImplementedCtorRef = methodDefinition.Module.ImportReference(
                                typeof(NotImplementedException).GetConstructor(new Type[] { typeof(string) })
                            );

                            ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, exceptionMessage));
                            ilprocessor.Append(ilprocessor.Create(OpCodes.Newobj, notImplementedCtorRef)); // Исправлено на notImplementedCtorRef
                            ilprocessor.Append(ilprocessor.Create(OpCodes.Throw));
                            bodyChanged = true;

                            if (!bodyChanged) // Если вдруг тело не изменили (не должно случиться здесь)
                            {
                                if (returnType.FullName == "System.Void")
                                {
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                                }
                                else if (returnType.IsValueType)
                                {
                                    var variable = new VariableDefinition(returnType);
                                    methodDefinition.Body.Variables.Add(variable);
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloca_S, variable));
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Initobj, returnType));
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                                }
                                else
                                {
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ldnull));
                                    ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                                }
                            }
                        }
                        methodDefinitionDic.Add(i, methodDefinition);
                        //method parameter
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            var parameterType = il2Cpp.types[parameterDef.typeIndex];
                            var parameterTypeRef = GetTypeReferenceWithByRef(methodDefinition, parameterType);
                            var parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes)parameterType.attrs, parameterTypeRef);
                            methodDefinition.Parameters.Add(parameterDefinition);
                            parameterDefinitionDic.Add(methodDef.parameterStart + j, parameterDefinition);
                            //ParameterDefault
                            if (metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + j, out var parameterDefault) && parameterDefault.dataIndex != -1)
                            {
                                if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var value))
                                {
                                    parameterDefinition.Constant = value;
                                }
                                else
                                {
                                    var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(metadataOffsetAttribute));
                                    var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{value:X}"));
                                    customAttribute.Fields.Add(offset);
                                    parameterDefinition.CustomAttributes.Add(customAttribute);
                                }
                            }
                        }
                        //methodAddress
                        if (!methodDefinition.IsAbstract)
                        {
                            var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                            if (methodPointer > 0)
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(addressAttribute));
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                var va = new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}"));
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttribute.Fields.Add(va);
                                if (methodDef.slot != ushort.MaxValue)
                                {
                                    var slot = new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString()));
                                    customAttribute.Fields.Add(slot);
                                }
                                methodDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //property
                    var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                    for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                    {
                        var propertyDef = metadata.propertyDefs[i];
                        var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                        TypeReference propertyType = null;
                        MethodDefinition GetMethod = null;
                        MethodDefinition SetMethod = null;
                        if (propertyDef.get >= 0)
                        {
                            GetMethod = methodDefinitionDic[typeDef.methodStart + propertyDef.get];
                            propertyType = GetMethod.ReturnType;
                        }
                        if (propertyDef.set >= 0)
                        {
                            SetMethod = methodDefinitionDic[typeDef.methodStart + propertyDef.set];
                            propertyType ??= SetMethod.Parameters[0].ParameterType;
                        }
                        var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes)propertyDef.attrs, propertyType)
                        {
                            GetMethod = GetMethod,
                            SetMethod = SetMethod
                        };
                        typeDefinition.Properties.Add(propertyDefinition);
                        propertyDefinitionDic.Add(i, propertyDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                            propertyDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                    //event
                    var eventEnd = typeDef.eventStart + typeDef.event_count;
                    for (var i = typeDef.eventStart; i < eventEnd; ++i)
                    {
                        var eventDef = metadata.eventDefs[i];
                        var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                        var eventType = il2Cpp.types[eventDef.typeIndex];
                        var eventTypeRef = GetTypeReference(typeDefinition, eventType);
                        var eventDefinition = new EventDefinition(eventName, (EventAttributes)eventType.attrs, eventTypeRef);
                        if (eventDef.add >= 0)
                            eventDefinition.AddMethod = methodDefinitionDic[typeDef.methodStart + eventDef.add];
                        if (eventDef.remove >= 0)
                            eventDefinition.RemoveMethod = methodDefinitionDic[typeDef.methodStart + eventDef.remove];
                        if (eventDef.raise >= 0)
                            eventDefinition.InvokeMethod = methodDefinitionDic[typeDef.methodStart + eventDef.raise];
                        typeDefinition.Events.Add(eventDefinition);
                        eventDefinitionDic.Add(i, eventDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{eventDef.token:X}")));
                            eventDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                }
            }
            // Новый проход для переименования полей, связанных со свойствами
            RenameBackingFields();
            //第三遍，添加CustomAttribute
            if (il2Cpp.Version > 20)
            {
                foreach (var imageDef in metadata.imageDefs)
                {
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int index = imageDef.typeStart; index < typeEnd; index++)
                    {
                        var typeDef = metadata.typeDefs[index];
                        var typeDefinition = typeDefinitionDic[typeDef];
                        //typeAttribute
                        CreateCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, typeDefinition.CustomAttributes);

                        //field
                        var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                        for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                        {
                            var fieldDef = metadata.fieldDefs[i];
                            var fieldDefinition = fieldDefinitionDic[i];
                            //fieldAttribute
                            CreateCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, fieldDefinition.CustomAttributes);
                        }

                        //method
                        var methodEnd = typeDef.methodStart + typeDef.method_count;
                        for (var i = typeDef.methodStart; i < methodEnd; ++i)
                        {
                            var methodDef = metadata.methodDefs[i];
                            var methodDefinition = methodDefinitionDic[i];
                            //methodAttribute
                            CreateCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, methodDefinition.CustomAttributes);

                            //method parameter
                            for (var j = 0; j < methodDef.parameterCount; ++j)
                            {
                                var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                                var parameterDefinition = parameterDefinitionDic[methodDef.parameterStart + j];
                                //parameterAttribute
                                CreateCustomAttribute(imageDef, parameterDef.customAttributeIndex, parameterDef.token, typeDefinition.Module, parameterDefinition.CustomAttributes);
                            }
                        }

                        //property
                        var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                        for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                        {
                            var propertyDef = metadata.propertyDefs[i];
                            var propertyDefinition = propertyDefinitionDic[i];
                            //propertyAttribute
                            CreateCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, typeDefinition.Module, propertyDefinition.CustomAttributes);
                        }

                        //event
                        var eventEnd = typeDef.eventStart + typeDef.event_count;
                        for (var i = typeDef.eventStart; i < eventEnd; ++i)
                        {
                            var eventDef = metadata.eventDefs[i];
                            var eventDefinition = eventDefinitionDic[i];
                            //eventAttribute
                            CreateCustomAttribute(imageDef, eventDef.customAttributeIndex, eventDef.token, typeDefinition.Module, eventDefinition.CustomAttributes);
                        }
                    }
                }
            }
        }

        private TypeReference GetTypeReferenceWithByRef(MemberReference memberReference, Il2CppType il2CppType)
        {
            var typeReference = GetTypeReference(memberReference, il2CppType);
            if (il2CppType.byref == 1)
            {
                return new ByReferenceType(typeReference);
            }
            else
            {
                return typeReference;
            }
        }

        private TypeReference GetTypeReference(MemberReference memberReference, Il2CppType il2CppType)
        {
            var moduleDefinition = memberReference.Module;
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return moduleDefinition.ImportReference(typeSystem.Object);
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return moduleDefinition.ImportReference(typeSystem.Void);
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return moduleDefinition.ImportReference(typeSystem.Boolean);
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return moduleDefinition.ImportReference(typeSystem.Char);
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return moduleDefinition.ImportReference(typeSystem.SByte);
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return moduleDefinition.ImportReference(typeSystem.Byte);
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return moduleDefinition.ImportReference(typeSystem.Int16);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return moduleDefinition.ImportReference(typeSystem.UInt16);
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return moduleDefinition.ImportReference(typeSystem.Int32);
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return moduleDefinition.ImportReference(typeSystem.UInt32);
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return moduleDefinition.ImportReference(typeSystem.IntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return moduleDefinition.ImportReference(typeSystem.UIntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return moduleDefinition.ImportReference(typeSystem.Int64);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return moduleDefinition.ImportReference(typeSystem.UInt64);
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return moduleDefinition.ImportReference(typeSystem.Single);
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return moduleDefinition.ImportReference(typeSystem.Double);
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return moduleDefinition.ImportReference(typeSystem.String);
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return moduleDefinition.ImportReference(typeSystem.TypedReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        var typeDefinition = typeDefinitionDic[typeDef];
                        return moduleDefinition.ImportReference(typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var oriType = il2Cpp.GetIl2CppType(arrayType.etype);
                        return new ArrayType(GetTypeReference(memberReference, oriType), arrayType.rank);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        var typeDefinition = typeDefinitionDic[typeDef];
                        var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                        var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(genericClass.context.class_inst);
                        var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                        foreach (var pointer in pointers)
                        {
                            var oriType = il2Cpp.GetIl2CppType(pointer);
                            genericInstanceType.GenericArguments.Add(GetTypeReference(memberReference, oriType));
                        }
                        return genericInstanceType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new ArrayType(GetTypeReference(memberReference, oriType));
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (memberReference is MethodDefinition methodDefinition)
                        {
                            return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), methodDefinition.DeclaringType);
                        }
                        var typeDefinition = (TypeDefinition)memberReference;
                        return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var methodDefinition = (MethodDefinition)memberReference;
                        return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), methodDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new PointerType(GetTypeReference(memberReference, oriType));
                    }
                default:
                    throw new NotSupportedException();
            }
        }

        private void CreateCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                try
                {
                    if (il2Cpp.Version < 29)
                    {
                        var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                        for (int i = 0; i < attributeTypeRange.count; i++)
                        {
                            var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                            var attributeType = il2Cpp.types[attributeTypeIndex];
                            var typeDef = executor.GetTypeDefinitionFromIl2CppType(attributeType);
                            var typeDefinition = typeDefinitionDic[typeDef];
                            if (!TryRestoreCustomAttribute(typeDefinition, moduleDefinition, customAttributes))
                            {
                                var methodPointer = executor.customAttributeGenerators[attributeIndex];
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(attributeAttribute));
                                var name = new CustomAttributeNamedArgument("Name", new CustomAttributeArgument(stringType, typeDefinition.Name));
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                customAttribute.Fields.Add(name);
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttributes.Add(customAttribute);
                            }
                        }
                    }
                    else
                    {
                        var startRange = metadata.attributeDataRanges[attributeIndex];
                        var endRange = metadata.attributeDataRanges[attributeIndex + 1];
                        metadata.Position = metadata.header.attributeDataOffset + startRange.startOffset;
                        var buff = metadata.ReadBytes((int)(endRange.startOffset - startRange.startOffset));
                        var reader = new CustomAttributeDataReader(executor, buff);
                        if (reader.Count != 0)
                        {
                            for (var i = 0; i < reader.Count; i++)
                            {
                                var visitor = reader.VisitCustomAttributeData();
                                var methodDefinition = methodDefinitionDic[visitor.CtorIndex];
                                var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(methodDefinition));
                                foreach (var argument in visitor.Arguments)
                                {
                                    var parameterDefinition = methodDefinition.Parameters[argument.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(parameterDefinition.ParameterType, argument.Value, methodDefinition);
                                    customAttribute.ConstructorArguments.Add(customAttributeArgument);
                                }
                                foreach (var field in visitor.Fields)
                                {
                                    var fieldDefinition = fieldDefinitionDic[field.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(fieldDefinition.FieldType, field.Value, fieldDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(fieldDefinition.Name, customAttributeArgument);
                                    customAttribute.Fields.Add(customAttributeNamedArgument);
                                }
                                foreach (var property in visitor.Properties)
                                {
                                    var propertyDefinition = propertyDefinitionDic[property.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(propertyDefinition.PropertyType, property.Value, propertyDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(propertyDefinition.Name, customAttributeArgument);
                                    customAttribute.Properties.Add(customAttributeNamedArgument);
                                }
                                customAttributes.Add(customAttribute);
                            }
                        }
                    }
                }
                catch
                {
                    Console.WriteLine($"ERROR: Error while restoring attributeIndex {attributeIndex}");
                }
            }
        }

        private static bool TryRestoreCustomAttribute(TypeDefinition attributeType, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            if (attributeType.Methods.Count == 1 && attributeType.Name != "CompilerGeneratedAttribute")
            {
                var methodDefinition = attributeType.Methods[0];
                if (methodDefinition.Name == ".ctor" && methodDefinition.Parameters.Count == 0)
                {
                    var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(methodDefinition));
                    customAttributes.Add(customAttribute);
                    return true;
                }
            }
            return false;
        }

        private GenericParameter CreateGenericParameter(Il2CppGenericParameter param, IGenericParameterProvider iGenericParameterProvider)
        {
            if (!genericParameterDic.TryGetValue(param, out var genericParameter))
            {
                var genericName = metadata.GetStringFromIndex(param.nameIndex);
                genericParameter = new GenericParameter(genericName, iGenericParameterProvider)
                {
                    Attributes = (GenericParameterAttributes)param.flags
                };
                genericParameterDic.Add(param, genericParameter);
                for (int i = 0; i < param.constraintsCount; ++i)
                {
                    var il2CppType = il2Cpp.types[metadata.constraintIndices[param.constraintsStart + i]];
                    genericParameter.Constraints.Add(new GenericParameterConstraint(GetTypeReference((MemberReference)iGenericParameterProvider, il2CppType)));
                }
            }
            return genericParameter;
        }

        private CustomAttributeArgument CreateCustomAttributeArgument(TypeReference typeReference, BlobValue blobValue, MemberReference memberReference)
        {
            var val = blobValue.Value;
            if (typeReference.FullName == "System.Object")
            {
                if (blobValue.il2CppTypeEnum == Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX)
                {
                    val = new CustomAttributeArgument(memberReference.Module.ImportReference(typeof(Type)), GetTypeReference(memberReference, (Il2CppType)val));
                }
                else
                {
                    val = new CustomAttributeArgument(GetBlobValueTypeReference(blobValue, memberReference), val);
                }
            }
            else if (val == null)
            {
                return new CustomAttributeArgument(typeReference, val);
            }
            else if (typeReference is ArrayType arrayType)
            {
                var arrayVal = (BlobValue[])val;
                var array = new CustomAttributeArgument[arrayVal.Length];
                var elementType = arrayType.ElementType;
                for (int i = 0; i < arrayVal.Length; i++)
                {
                    array[i] = CreateCustomAttributeArgument(elementType, arrayVal[i], memberReference);
                }
                val = array;
            }
            else if (typeReference.FullName == "System.Type")
            {
                val = GetTypeReference(memberReference, (Il2CppType)val);
            }
            return new CustomAttributeArgument(typeReference, val);
        }

        private TypeReference GetBlobValueTypeReference(BlobValue blobValue, MemberReference memberReference)
        {
            if (blobValue.EnumType != null)
            {
                return GetTypeReference(memberReference, blobValue.EnumType);
            }
            var il2CppType = new Il2CppType
            {
                type = blobValue.il2CppTypeEnum
            };
            return GetTypeReference(memberReference, il2CppType);
        }

        private void RenameBackingFields()
        {
            foreach (var assembly in Assemblies)
            {
                foreach (var module in assembly.Modules)
                {
                    foreach (var typeDef in module.GetAllTypes()) // Mono.Cecil.ModuleDefinition.GetAllTypes() возвращает все типы, включая вложенные
                    {
                        if (!typeDef.HasProperties)
                            continue;

                        var fieldsToRename = new List<(FieldDefinition field, string newName)>();

                        foreach (var propDef in typeDef.Properties)
                        {
                            // Ищем стандартное имя для backing field (например, <PropertyName>k__BackingField)
                            var backingFieldName = $"<{propDef.Name}>k__BackingField";
                            var field = typeDef.Fields.FirstOrDefault(f => f.Name == backingFieldName);

                            if (field != null)
                            {
                                // Предлагаемое новое имя. Можно обсудить другой формат.
                                // Например, сделать его приватным и назвать _propertyName
                                string newName = $"__prop_{propDef.Name}";

                                // Проверка на конфликт имен (маловероятно для такого префикса, но все же)
                                if (typeDef.Fields.Any(f => f.Name == newName) && field.Name != newName)
                                {
                                    // Если конфликт, добавляем суффикс
                                    int counter = 1;
                                    string tempName;
                                    do
                                    {
                                        tempName = $"{newName}_{counter++}";
                                    } while (typeDef.Fields.Any(f => f.Name == tempName));
                                    newName = tempName;
                                }
                                fieldsToRename.Add((field, newName));
                            }
                        }

                        foreach(var (field, newName) in fieldsToRename)
                        {
                            field.Name = newName;
                            // Можно также рассмотреть изменение видимости поля на private,
                            // так как backing fields обычно приватные.
                            // field.IsPrivate = true;
                            // field.IsPublic = false; // и т.д. в зависимости от исходной видимости
                        }
                    }
                }
            }
        }
    }
}
