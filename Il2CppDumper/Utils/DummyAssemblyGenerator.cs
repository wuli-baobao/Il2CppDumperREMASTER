using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Gee.External.Capstone.X86;
using Il2CppDumper.lifting;
using Il2CppDumper.lifting.Operation;
using Il2CppDumper.UppdateLowCode;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
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
        private readonly MethodDefinition addressAttribute;
        private readonly MethodDefinition fieldOffsetAttribute;
        private readonly MethodDefinition metadataOffsetAttribute;
        private readonly MethodDefinition tokenAttribute;
        private readonly TypeReference stringType;
        private readonly TypeSystem typeSystem;
        private readonly Dictionary<ulong, MethodDefinition> addressToMethodMap = new();
        private readonly Dictionary<int, FieldDefinition> fieldDefinitionDic = new();
        private readonly Dictionary<int, PropertyDefinition> propertyDefinitionDic = new();
        private readonly Dictionary<int, MethodDefinition> methodDefinitionDic = new();

        public DummyAssemblyGenerator(Il2CppExecutor il2CppExecutor, bool addToken)
        {
            executor = il2CppExecutor ?? throw new ArgumentNullException(nameof(il2CppExecutor));
            metadata = il2CppExecutor.metadata ?? throw new ArgumentNullException(nameof(il2CppExecutor.metadata));
            il2Cpp = il2CppExecutor.il2Cpp ?? throw new ArgumentNullException(nameof(il2CppExecutor.il2Cpp));

            // Загрузка Il2CppDummyDll
            AssemblyDefinition il2CppDummyDll;
            try
            {
                il2CppDummyDll = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource1.Il2CppDummyDll));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DummyAssemblyGenerator] Ошибка загрузки Il2CppDummyDll: {ex.Message}\nStackTrace: {ex.StackTrace}");
                throw new InvalidOperationException("Не удалось загрузить Il2CppDummyDll.", ex);
            }

            if (il2CppDummyDll?.MainModule?.TypeSystem?.Object == null)
            {
                Console.WriteLine("[DummyAssemblyGenerator] Ошибка: TypeSystem.Object в Il2CppDummyDll равен null.");
                throw new InvalidOperationException("TypeSystem.Object в Il2CppDummyDll равен null.");
            }

            Assemblies.Add(il2CppDummyDll);
            var dummyMD = il2CppDummyDll.MainModule;

            if (dummyMD.TypeSystem == null || dummyMD.TypeSystem.Object == null)
            {
                Console.WriteLine("[DummyAssemblyGenerator] Ошибка: TypeSystem или TypeSystem.Object в dummyMD равен null.");
                throw new InvalidOperationException("TypeSystem или TypeSystem.Object в dummyMD равен null.");
            }

            stringType = dummyMD.TypeSystem.String;
            typeSystem = dummyMD.TypeSystem;

            // Проверка наличия необходимых типов атрибутов
            try
            {
                var addressAttributeType = dummyMD.Types.FirstOrDefault(x => x.Name == "AddressAttribute");
                if (addressAttributeType == null || !addressAttributeType.Methods.Any())
                {
                    throw new InvalidOperationException("Тип AddressAttribute не найден или не содержит методов в Il2CppDummyDll.");
                }
                addressAttribute = addressAttributeType.Methods[0];

                var fieldOffsetAttributeType = dummyMD.Types.FirstOrDefault(x => x.Name == "FieldOffsetAttribute");
                if (fieldOffsetAttributeType == null || !fieldOffsetAttributeType.Methods.Any())
                {
                    throw new InvalidOperationException("Тип FieldOffsetAttribute не найден или не содержит методов в Il2CppDummyDll.");
                }
                fieldOffsetAttribute = fieldOffsetAttributeType.Methods[0];

                var attributeAttributeType = dummyMD.Types.FirstOrDefault(x => x.Name == "AttributeAttribute");
                if (attributeAttributeType == null || !attributeAttributeType.Methods.Any())
                {
                    throw new InvalidOperationException("Тип AttributeAttribute не найден или не содержит методов в Il2CppDummyDll.");
                }
                attributeAttribute = attributeAttributeType.Methods[0];

                var metadataOffsetAttributeType = dummyMD.Types.FirstOrDefault(x => x.Name == "MetadataOffsetAttribute");
                if (metadataOffsetAttributeType == null || !metadataOffsetAttributeType.Methods.Any())
                {
                    throw new InvalidOperationException("Тип MetadataOffsetAttribute не найден или не содержит методов в Il2CppDummyDll.");
                }
                metadataOffsetAttribute = metadataOffsetAttributeType.Methods[0];

                var tokenAttributeType = dummyMD.Types.FirstOrDefault(x => x.Name == "TokenAttribute");
                if (tokenAttributeType == null || !tokenAttributeType.Methods.Any())
                {
                    throw new InvalidOperationException("Тип TokenAttribute не найден или не содержит методов в Il2CppDummyDll.");
                }
                tokenAttribute = tokenAttributeType.Methods[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DummyAssemblyGenerator] Ошибка доступа к типам атрибутов: {ex.Message}\nStackTrace: {ex.StackTrace}");
                throw new InvalidOperationException("Не удалось инициализировать типы атрибутов из Il2CppDummyDll.", ex);
            }

            // Инициализация резолвера
            var resolver = new MyAssemblyResolver();
            var frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location); // Путь к .NET Framework
            resolver.AddSearchDirectory(frameworkDir);
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            resolver.Register(il2CppDummyDll);

            // Загрузка mscorlib.dll из директории .NET Framework
            AssemblyDefinition mscorlib;
            TypeReference systemObjectType;
            try
            {
                var mscorlibPath = Path.Combine(frameworkDir, "mscorlib.dll");
                if (!File.Exists(mscorlibPath))
                {
                    Console.WriteLine($"[DummyAssemblyGenerator] Ошибка: mscorlib.dll не найден по пути {mscorlibPath}.");
                    throw new FileNotFoundException($"mscorlib.dll не найден по пути {mscorlibPath}.");
                }
                mscorlib = AssemblyDefinition.ReadAssembly(mscorlibPath);
                if (mscorlib?.MainModule?.TypeSystem?.Object == null)
                {
                    Console.WriteLine("[DummyAssemblyGenerator] Ошибка: TypeSystem.Object в mscorlib.dll равен null.");
                    throw new InvalidOperationException("TypeSystem.Object в mscorlib.dll равен null.");
                }
                systemObjectType = mscorlib.MainModule.TypeSystem.Object;
                resolver.Register(mscorlib);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DummyAssemblyGenerator] Ошибка загрузки mscorlib.dll: {ex.Message}\nStackTrace: {ex.StackTrace}");
                throw new InvalidOperationException("Не удалось загрузить mscorlib.dll из директории .NET Framework.", ex);
            }

            var parameterDefinitionDic = new Dictionary<int, ParameterDefinition>();
            var eventDefinitionDic = new Dictionary<int, EventDefinition>();

            // Создание сборок
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var aname = metadata.assemblyDefs[imageDef.assemblyIndex].aname;
                var assemblyName = metadata.GetStringFromIndex(aname.nameIndex);
                // Избегаем конфликта имени с mscorlib
                if (assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
                {
                    assemblyName = $"Generated_{assemblyName}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    Console.WriteLine($"[DummyAssemblyGenerator] Переименована сборка 'mscorlib' в '{assemblyName}' для избежания конфликта.");
                }
                Version vers;
                if (aname.build >= 0)
                {
                    vers = new Version(aname.major, aname.minor, aname.build, aname.revision);
                }
                else
                {
                    vers = new Version(4, 0, 0, 0); // Версия по умолчанию, совместимая с mscorlib
                }
                var assemblyNameDef = new AssemblyNameDefinition(assemblyName, vers);
                AssemblyDefinition assemblyDefinition;
                try
                {
                    assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyNameDef, imageName, moduleParameters);
                    var module = assemblyDefinition.MainModule;
                    // Добавление ссылки на mscorlib.dll
                    var mscorlibReference = new AssemblyNameReference("mscorlib", new Version(4, 0, 0, 0));
                    if (!module.AssemblyReferences.Any(r => r.Name == "mscorlib"))
                    {
                        module.AssemblyReferences.Add(mscorlibReference);
                        Console.WriteLine($"[DummyAssemblyGenerator] Добавлена ссылка на mscorlib для сборки {assemblyName}.");
                    }
                    // Импорт System.Object из mscorlib.dll
                    var importedObjectType = module.ImportReference(systemObjectType);
                    if (importedObjectType == null)
                    {
                        Console.WriteLine($"[DummyAssemblyGenerator] Ошибка: Не удалось импортировать System.Object для сборки {assemblyName}.");
                        throw new InvalidOperationException($"Не удалось импортировать System.Object для сборки {assemblyName}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DummyAssemblyGenerator] Ошибка создания сборки {assemblyName}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    throw new InvalidOperationException($"Не удалось создать сборку {assemblyName}.", ex);
                }

                resolver.Register(assemblyDefinition);
                Assemblies.Add(assemblyDefinition);
                var moduleDefinition = assemblyDefinition.MainModule;

                // Проверка TypeSystem для новой сборки
                if (moduleDefinition.TypeSystem == null || moduleDefinition.TypeSystem.Object == null)
                {
                    Console.WriteLine($"[DummyAssemblyGenerator] Ошибка: TypeSystem или TypeSystem.Object равен null для сборки {assemblyName}.");
                    throw new InvalidOperationException($"TypeSystem или TypeSystem.Object равен null для сборки {assemblyName}.");
                }

                moduleDefinition.Types.Clear(); // Очистка автоматически созданного класса <Module>
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

            // Обработка вложенных типов
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    // Вложенные типы
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
                            var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                            var methodRVA = methodPointer > 0 ? il2Cpp.GetRVA(methodPointer) : 0;
                            var arch = il2Cpp.GetArchitectureType(); // Переименовали переменную

                            Console.WriteLine($"[Method Processing] Method: {methodDefinition.FullName}, Pointer: 0x{methodPointer:X}, RVA: 0x{methodRVA:X}, Architecture: {arch}");

                            if (methodPointer > 0 && arch != ArchitectureType.Unknown)
                            {
                                // Получаем размер метода
                                uint readMethodSize = 0;
                                var sortedRVAs = executor.GetSortedFunctionRVAs();
                                if (Il2CppExecutor.TryGetMethodSize(methodRVA, sortedRVAs, out var determinedSize) && determinedSize > 0)
                                {
                                    readMethodSize = determinedSize;
                                }

                                Console.WriteLine($"[Method Processing] Determined method size: {readMethodSize} bytes");

                                if (readMethodSize > 0)
                                {
                                    try
                                    {
                                        ulong fileOffset = il2Cpp.MapVATR(methodPointer);
                                        Console.WriteLine($"[Method Processing] File offset: 0x{fileOffset:X}");

                                        if (fileOffset > 0)
                                        {
                                            byte[] methodCodeBytes = null; // Переименовали переменную

                                            // Читаем байты метода
                                            lock (il2Cpp)
                                            {
                                                il2Cpp.Position = fileOffset;
                                                methodCodeBytes = il2Cpp.ReadBytes((int)readMethodSize);
                                            }

                                            Console.WriteLine($"[Method Processing] Read {methodCodeBytes.Length} bytes from file");

                                            // Генерируем тело метода
                                            GenerateMethodBody(methodDefinition, methodCodeBytes, methodPointer, arch);
                                        }
                                        else
                                        {
                                            Console.WriteLine($"[Method Processing] Invalid file offset for method {methodDefinition.FullName}");
                                            GenerateFallbackBody(methodDefinition, "Invalid file offset");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[Method Processing] Error reading bytes for {methodDefinition.FullName}: {ex}");
                                        GenerateFallbackBody(methodDefinition, $"Error reading method bytes: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[Method Processing] Could not determine method size for {methodDefinition.FullName}");
                                    GenerateFallbackBody(methodDefinition, "Could not determine method size");
                                }
                            }
                            else
                            {
                                string reason = methodPointer <= 0 ? "Method pointer not available" : "Unknown architecture";
                                Console.WriteLine($"[Method Processing] Skipping {methodDefinition.FullName}: {reason}");
                                GenerateFallbackBody(methodDefinition, reason);
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
            if (il2CppType == null) throw new ArgumentNullException(nameof(il2CppType));
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

        private void GenerateMethodBody(MethodDefinition methodDefinition, byte[] codeBytes, ulong methodPointer, ArchitectureType architecture)
        {
            if (methodDefinition == null || methodDefinition.Body == null)
            {
                Console.WriteLine("[GenerateMethodBody] Error: methodDefinition or its Body is null.");
                GenerateFallbackBody(methodDefinition, "methodDefinition or Body is null");
                return;
            }

            try
            {
                var ilprocessor = methodDefinition.Body.GetILProcessor();
                if (ilprocessor == null)
                {
                    Console.WriteLine("[GenerateMethodBody] Error: ILProcessor is null.");
                    GenerateFallbackBody(methodDefinition, "ILProcessor is null");
                    return;
                }

                ilprocessor.Body.Instructions.Clear();
                ilprocessor.Body.Variables.Clear();
                ilprocessor.Body.ExceptionHandlers.Clear();

                int pointerSize = (architecture == ArchitectureType.X86_64 || architecture == ArchitectureType.ARM64) ? 8 : 4;

                var disassembledInstructions = MethodDisassembler.Disassemble(codeBytes, methodPointer, architecture);
                if (disassembledInstructions == null || !disassembledInstructions.Any())
                {
                    Console.WriteLine("[GenerateMethodBody] Disassembly failed or no instructions.");
                    GenerateFallbackBody(methodDefinition, "Disassembly failed or no instructions");
                    return;
                }

                if (methodDefinition.Module == null || methodDefinition.Module.TypeSystem == null)
                {
                    Console.WriteLine($"[GenerateMethodBody] Error: Module or TypeSystem is null for {methodDefinition.FullName}");
                    GenerateFallbackBody(methodDefinition, "Module or TypeSystem is null");
                    return;
                }

                var lifter = new Lifter(methodDefinition.Module, pointerSize);
                if (lifter == null)
                {
                    Console.WriteLine("[GenerateMethodBody] Error: Lifter is null.");
                    GenerateFallbackBody(methodDefinition, "Lifter is null");
                    return;
                }

                List<IROperation> liftedOperations;
                try
                {
                    liftedOperations = lifter.Lift(disassembledInstructions);
                }
                catch (Exception liftEx)
                {
                    Console.WriteLine($"[GenerateMethodBody] Lifting failed: {liftEx}");
                    liftedOperations = new List<IROperation>
            {
                new ErrorOperation { Address = methodPointer, Message = $"Lifting failed: {liftEx.Message}" }
            };
                }

                if (liftedOperations == null)
                {
                    Console.WriteLine("[GenerateMethodBody] Lifted operations are null.");
                    GenerateFallbackBody(methodDefinition, "Lifted operations are null");
                    return;
                }

                var cfAnalyzer = new ControlFlowAnalyzer();
                var blocks = cfAnalyzer.StructureBlocks(liftedOperations);
                if (blocks == null)
                {
                    Console.WriteLine("[GenerateMethodBody] Control flow analysis failed.");
                    GenerateFallbackBody(methodDefinition, "Control flow analysis failed");
                    return;
                }

                var cilGenerator = new CilGenerator(ilprocessor, methodDefinition, this);
                cilGenerator.Generate(liftedOperations);
                DummyAssemblyGenerator.ReconstructExceptionHandling(blocks, cilGenerator, methodDefinition);

                Console.WriteLine($"[GenerateMethodBody] Successfully generated CIL for {methodDefinition.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GenerateMethodBody] Error generating method body for {methodDefinition.FullName}: {ex}");
                GenerateFallbackBody(methodDefinition, $"Error generating method: {ex.Message}");
            }
        }

        private static void GenerateFallbackBody(MethodDefinition methodDefinition, string errorMessage)
        {
            var ilProc = methodDefinition.Body.GetILProcessor();
            ilProc.Body.Instructions.Clear();
            ilProc.Emit(OpCodes.Ldstr, $"Method generation failed: {errorMessage}. Method: {methodDefinition.FullName}");
            ilProc.Emit(OpCodes.Newobj, methodDefinition.Module.ImportReference(
                typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })));
            ilProc.Emit(OpCodes.Throw);
        }

        private static void ReconstructExceptionHandling(List<IRBlock> blocks, CilGenerator cilGenerator, MethodDefinition methodDefinition)
        {
            // 1. Identify protected blocks (try blocks)
            var tryStarts = new List<IRBlock>();
            var tryEnds = new List<IRBlock>();

            // 2. Identify handler blocks (catch/finally)
            var handlerStarts = new List<IRBlock>();

            // 3. Create ExceptionHandlers
            foreach (var block in blocks)
            {
                // Pattern matching for exception handling
                if (block.Operations.Any(op => op is CallOperation call &&
                    call.Method?.Name == "BeginExceptionBlock"))
                {
                    tryStarts.Add(block);
                }

                if (block.Operations.Any(op => op is CallOperation call &&
                    call.Method?.Name == "EndExceptionBlock"))
                {
                    tryEnds.Add(block);
                }

                if (block.Operations.Any(op => op is CallOperation call &&
                    call.Method?.Name.Contains("Catch") == true))
                {
                    handlerStarts.Add(block);
                }
            }

            // Create exception handling clauses
            for (int i = 0; i < tryStarts.Count; i++)
            {
                if (i < tryEnds.Count && i < handlerStarts.Count)
                {
                    var tryStart = cilGenerator.GetInstruction(tryStarts[i].StartAddress);
                    var tryEnd = cilGenerator.GetInstruction(tryEnds[i].StartAddress);
                    var handlerStart = cilGenerator.GetInstruction(handlerStarts[i].StartAddress);

                    if (tryStart != null && tryEnd != null && handlerStart != null)
                    {
                        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
                        {
                            TryStart = tryStart,
                            TryEnd = tryEnd,
                            HandlerStart = handlerStart,
                            HandlerEnd = handlerStart.Next ?? tryEnd,
                            CatchType = methodDefinition.Module.ImportReference(typeof(Exception))
                        };

                        cilGenerator.AddExceptionHandler(handler);
                    }
                }
            }
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

        public MethodDefinition ResolveMethodByVA(ulong va)
        {
            if (addressToMethodMap.TryGetValue(va, out var method))
                return method;
            Console.WriteLine($"[ResolveMethodByVA] No method found for VA: 0x{va:X}");
            return null;
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
                    // Получаем все типы включая вложенные
                    foreach (var typeDef in module.GetAllTypes())
                    {
                        if (!typeDef.HasProperties || !typeDef.HasFields)
                            continue;

                        var fieldsToRename = new List<(FieldDefinition field, string newName)>();

                        foreach (var propDef in typeDef.Properties)
                        {
                            // Обычное имя для backing field автосвойства
                            var backingFieldName = $"<{propDef.Name}>k__BackingField";

                            // Находим поле с таким именем
                            var field = typeDef.Fields.FirstOrDefault(f => f.Name == backingFieldName);
                            if (field == null)
                                continue;

                            // Формируем новое имя
                            string baseNewName = $"__prop_{propDef.Name}";

                            // Проверяем на конфликт имен среди полей в этом типе (кроме самого текущего поля)
                            if (typeDef.Fields.Any(f => f.Name == baseNewName && f != field))
                            {
                                int counter = 1;
                                string tempName;
                                do
                                {
                                    tempName = $"{baseNewName}_{counter++}";
                                } while (typeDef.Fields.Any(f => f.Name == tempName && f != field));
                                baseNewName = tempName;
                            }

                            fieldsToRename.Add((field, baseNewName));
                        }

                        // Переименовываем поля уже после обхода свойств,
                        // чтобы не было конфликтов во время итерации
                        foreach (var (field, newName) in fieldsToRename)
                        {
                            field.Name = newName;
                        }
                    }
                }
            }
        }
    }
}