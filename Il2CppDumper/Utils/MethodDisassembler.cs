using System;
using System.Collections.Generic;
using System.Linq;
// Используем пространства имен из Gee.External.Capstone
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Gee.External.Capstone.X86;

namespace Il2CppDumper
{
    public class DisassembledInstruction
    {
        public ulong Address { get; set; }
        public short Size { get; set; } // CapstoneInstruction имеет Size как short
        public string Mnemonic { get; set; }
        public string Operands { get; set; }
        public byte[] Bytes { get; set; }
        // Для Gee.External.Capstone, детали хранятся в самой инструкции, специфичной для архитектуры
        // Мы можем хранить базовый CapstoneInstruction, а при анализе приводить к нужному типу.
        public CapstoneInstruction PlatformSpecificInstruction { get; set; }

        public override string ToString()
        {
            return $"0x{Address:X}: {Mnemonic} {Operands}";
        }
    }

    public static class MethodDisassembler
    {
        public static List<DisassembledInstruction> Disassemble(byte[] codeBytes, ulong baseAddress, ArchitectureType architecture)
        {
            var instructionsResult = new List<DisassembledInstruction>();
            if (codeBytes == null || codeBytes.Length == 0) return instructionsResult;

            AbstractDisassembler disassembler = null;
            CapstoneInstruction[] capstoneInstructions = null;
            try
            {
                switch (architecture)
                {
                    case ArchitectureType.X86_64:
var x64Disassembler = CapstoneX86Disassembler.CreateX86Disassembler(X86DisassembleMode.Bit64);
                        x64Disassembler.EnableInstructionDetails = true;
                        capstoneInstructions = x64Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        disassembler = x64Disassembler;
                        break;
                    case ArchitectureType.X86_32:
                        var x86Disassembler = CapstoneX86Disassembler.CreateX86Disassembler(X86DisassembleMode.Bit32);
                        x86Disassembler.EnableInstructionDetails = true;
                        capstoneInstructions = x86Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        disassembler = x86Disassembler;
                        break;
                    case ArchitectureType.ARM64:
                        var arm64Disassembler = CapstoneArm64Disassembler.CreateArm64Disassembler(Arm64DisassembleMode.Arm);
                        arm64Disassembler.EnableInstructionDetails = true;
                        capstoneInstructions = arm64Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        disassembler = arm64Disassembler;
                        break;
                    case ArchitectureType.ARM32:
                        var arm32Disassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
                        // Для Thumb:
                        // if (isThumb) arm32Disassembler.DisassembleMode = ArmDisassembleMode.Thumb;
                        arm32Disassembler.EnableInstructionDetails = true;
                        capstoneInstructions = arm32Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        disassembler = arm32Disassembler;
                        break;
                    default:
                        Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Unsupported or Unknown architecture: {architecture}");
                        return instructionsResult;
                }

                if (capstoneInstructions != null)
                {
                    foreach (var instr in capstoneInstructions)
                    {
                        instructionsResult.Add(new DisassembledInstruction
                        {
                            Address = (ulong)instr.Address,
                            Size = instr.Size, // У CapstoneInstruction есть Size
                            Mnemonic = instr.Mnemonic,
                            Operands = instr.Operand, // У CapstoneInstruction есть Operand
                            Bytes = instr.Bytes,    // У CapstoneInstruction есть Bytes
                            PlatformSpecificInstruction = instr // Сохраняем всю инструкцию для доступа к деталям
                        });
                    }
                }
            }
            catch (DllNotFoundException dllEx)
            {
Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Native library (capstone.dll/libcapstone.so/libcapstone.dylib) not found. Error: {dllEx.Message}");
            }
            catch (CapstoneException capEx)
            {
                 Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Capstone error for arch {architecture}: {capEx.Message} (ErrorCode: {capEx.NativeErrorCode})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: General error for arch {architecture}: {ex.Message}");
            }
            finally
            {
                (disassembler as IDisposable)?.Dispose();
            }

            return instructionsResult;
        }
    }
}
