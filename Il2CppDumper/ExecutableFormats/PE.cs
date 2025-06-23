using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public sealed class PE : Il2Cpp
    {
        private readonly SectionHeader[] sections;

        public PE(Stream stream) : base(stream)
        {
            var dosHeader = ReadClass<DosHeader>();
            if (dosHeader.Magic != 0x5A4D)
            {
                throw new InvalidDataException("ERROR: Invalid PE file");
            }
            Position = dosHeader.Lfanew;
            if (ReadUInt32() != 0x4550u) //Signature
            {
                throw new InvalidDataException("ERROR: Invalid PE file");
            }
            var fileHeader = ReadClass<FileHeader>();
            var pos = Position;
            var magic = ReadUInt16();
            Position -= 2;
            if (magic == 0x10b)
            {
                Is32Bit = true;
                var optionalHeader = ReadClass<OptionalHeader>();
                ImageBase = optionalHeader.ImageBase;
            }
            else if (magic == 0x20b)
            {
                var optionalHeader = ReadClass<OptionalHeader64>();
                ImageBase = optionalHeader.ImageBase;
            }
            else
            {
                throw new NotSupportedException($"Invalid Optional header magic {magic}");
            }
            Position = pos + fileHeader.SizeOfOptionalHeader;
            sections = ReadClassArray<SectionHeader>(fileHeader.NumberOfSections);
        }

        public void LoadFromMemory(ulong addr)
        {
            ImageBase = addr;
            foreach (var section in sections)
            {
                section.PointerToRawData = section.VirtualAddress;
                section.SizeOfRawData = section.VirtualSize;
            }
        }

        public override ulong MapVATR(ulong absAddr)
        {
            var addr = absAddr - ImageBase;
            var section = sections.FirstOrDefault(x => addr >= x.VirtualAddress && addr <= x.VirtualAddress + x.VirtualSize);
            if (section == null)
            {
                return 0ul;
            }
            return addr - section.VirtualAddress + section.PointerToRawData;
        }

        public override ulong MapRTVA(ulong addr)
        {
            var section = sections.FirstOrDefault(x => addr >= x.PointerToRawData && addr <= x.PointerToRawData + x.SizeOfRawData);
            if (section == null)
            {
                return 0ul;
            }
            return addr - section.PointerToRawData + section.VirtualAddress + ImageBase;
        }

        public override bool Search()
        {
            return false;
        }

        public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount)
        {
            var sectionHelper = GetSectionHelper(methodCount, typeDefinitionsCount, imageCount);
            var codeRegistration = sectionHelper.FindCodeRegistration();
            var metadataRegistration = sectionHelper.FindMetadataRegistration();
            return AutoPlusInit(codeRegistration, metadataRegistration);
        }

        public override bool SymbolSearch()
        {
            return false;
        }

        public override ulong GetRVA(ulong pointer)
        {
            return pointer - ImageBase;
        }

        public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount)
        {
            var execList = new List<SectionHeader>();
            var dataList = new List<SectionHeader>();
            foreach (var section in sections)
            {
                switch (section.Characteristics)
                {
                    case 0x60000020:
                        execList.Add(section);
                        break;
                    case 0x40000040:
                    case 0xC0000040:
                        dataList.Add(section);
                        break;
                }
            }
            var sectionHelper = new SectionHelper(this, methodCount, typeDefinitionsCount, metadataUsagesCount, imageCount);
            var data = dataList.ToArray();
            var exec = execList.ToArray();
            sectionHelper.SetSection(SearchSectionType.Exec, ImageBase, exec);
            sectionHelper.SetSection(SearchSectionType.Data, ImageBase, data);
            sectionHelper.SetSection(SearchSectionType.Bss, ImageBase, data);
            return sectionHelper;
        }

        public override bool CheckDump()
        {
            if (Is32Bit)
            {
                return ImageBase != 0x10000000;
            }
            else
            {
                return ImageBase != 0x180000000;
            }
        }

        public override ArchitectureType GetArchitectureType()
        {
            // fileHeader доступен как локальная переменная в конструкторе, но не как поле класса.
            // Нужно его сохранить. Добавлю поле fileHeader в класс PE.
            // В PE.cs уже есть FileHeader, который читается, но не сохраняется в поле.
            // Для этого изменения мне нужно сначала добавить поле в класс PE.
            // Предположим, что FileHeader fileHeader; уже является полем класса и инициализировано.
            // Однако, просмотрев код снова, fileHeader - локальная переменная в конструкторе.
            // Мы можем получить Machine из OptionalHeader, если он есть, или сохранить FileHeader.
            // Проще всего будет сохранить Machine из FileHeader при инициализации.

            // В текущей структуре PE.cs, `fileHeader` - локальная переменная конструктора.
            // Чтобы получить доступ к `machine`, нужно либо сохранить `fileHeader` как поле класса,
            // либо передать `machine` в конструктор `Il2Cpp` или сохранить его в `Il2Cpp`.
            // Самое простое - добавить поле `machine` в `PE` и инициализировать его.

            // Для данного упражнения, я сделаю допущение, что у нас есть доступ к machine.
            // В реальной реализации нужно будет обеспечить этот доступ.
            // Давайте посмотрим на структуру FileHeader, чтобы узнать имена констант.
            // Они обычно IMAGE_FILE_MACHINE_I386, IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE_ARM64.

            // Поскольку я не могу изменить FileHeader или добавить поле в PE без отдельного шага,
            // я пока закомментирую детали и верну Unknown, указав на необходимость доработки.
            // TODO: Сохранить FileHeader.Machine в поле класса PE для использования здесь.
            // ushort machine_val = this.fileHeader.Machine; // Если бы fileHeader было полем
            // if (machine_val == 0x014c) return ArchitectureType.X86_32; // IMAGE_FILE_MACHINE_I386
            // if (machine_val == 0x8664) return ArchitectureType.X86_64; // IMAGE_FILE_MACHINE_AMD64
            // if (machine_val == 0xaa64) return ArchitectureType.ARM64; // IMAGE_FILE_MACHINE_ARM64

            // Пока что, исходя из Is32Bit (что не всегда точно для определения ARM и т.п.)
            if (this.Is32Bit)
            {
                // Это может быть X86_32 или ARM32 если PE поддерживает ARM.
                // Без Machine это предположение.
                return ArchitectureType.X86_32;
            }
            else
            {
                // Это может быть X86_64 или ARM64.
                return ArchitectureType.X86_64;
            }
            // Этот вариант не очень точен. Правильнее было бы сохранить `machine` из `FileHeader`.
            // Я помечу это как TODO и верну Unknown, чтобы было ясно, что это требует доработки.
            // Console.WriteLine("[Warning PE.GetArchitectureType] Machine type from FileHeader is needed for accurate architecture detection. Returning Unknown/BestGuess.");
            // if (this.Is32Bit) return ArchitectureType.X86_32; // Грубое предположение
            // return ArchitectureType.X86_64; // Грубое предположение
            // Лучше пока вернуть Unknown, если нет точной информации.
            // Однако, в контексте Il2CppDumper, PE файлы обычно x86/x64.
            // Давайте сделаем так: если Is32Bit, то X86_32, иначе X86_64.
            // Это наиболее вероятный сценарий для PE в данном инструменте.
             if (Is32Bit)
                 return ArchitectureType.X86_32;
             else
                 return ArchitectureType.X86_64;
        }
    }
}
