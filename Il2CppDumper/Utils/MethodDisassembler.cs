using System;
using System.Collections.Generic;
using Capstone;
using Capstone.X86;
using Capstone.Arm;
using Capstone.Arm64;

// Предполагается, что enum ArchitectureType находится в том же namespace или доступен
// namespace Il2CppDumper.Utils // Если ArchitectureType.cs в этом же неймспейсе

namespace Il2CppDumper // Или корневой namespace, если ArchitectureType там
{
    public class DisassembledInstruction
    {
        public ulong Address { get; set; }
        public int Size { get; set; }
        public string Mnemonic { get; set; }
        public string Operands { get; set; }
        public byte[] Bytes { get; set; }
        public Capstone.InstructionDetails Details { get; set; } // Добавлено поле

        public override string ToString()
        {
            return $"0x{Address:X}: {Mnemonic} {Operands}";
        }
    }

    public static class MethodDisassembler
    {
        public static List<DisassembledInstruction> Disassemble(byte[] codeBytes, ulong baseAddress, ArchitectureType architecture)
        {
            var instructions = new List<DisassembledInstruction>();
            if (codeBytes == null || codeBytes.Length == 0)
                return instructions;

            CapstoneDisassembler disassembler = null;
            try
            {
                switch (architecture)
                {
                    case ArchitectureType.X86_64:
                        disassembler = new CapstoneX86Disassembler(DisassembleArchitecture.X86, DisassembleMode.Bit64);
                        break;
                    case ArchitectureType.X86_32:
                        disassembler = new CapstoneX86Disassembler(DisassembleArchitecture.X86, DisassembleMode.Bit32);
                        break;
                    case ArchitectureType.ARM64:
                        disassembler = new CapstoneArm64Disassembler(DisassembleArchitecture.Arm64, DisassembleMode.Arm);
                        break;
                    case ArchitectureType.ARM32:
                         disassembler = new CapstoneArmDisassembler(DisassembleArchitecture.Arm, DisassembleMode.Arm);
                        // TODO: Уточнить режим для ARM Thumb (многие Il2Cpp игры на ARM используют Thumb-2)
                        // По умолчанию CapstoneArmDisassembler может использовать основной режим ARM.
                        // Для Thumb: disassembler.Mode = DisassembleMode.Thumb;
                        // Для автоматического определения (если поддерживается Capstone версией): disassembler.Mode = DisassembleMode.Arm | DisassembleMode.Thumb;
                        break;
                    default:
                        Console.WriteLine($"[MethodDisassembler] Unsupported or Unknown architecture: {architecture}");
                        return instructions;
                }

                disassembler.EnableDetails = true; // Включаем детализацию для всех архитектур

                var capstoneInstructions = disassembler.Disassemble(codeBytes, (long)baseAddress);

                foreach (var instr in capstoneInstructions)
                {
                    instructions.Add(new DisassembledInstruction
                    {
                        Address = (ulong)instr.Address,
                        Size = instr.Bytes.Length,
                        Mnemonic = instr.Mnemonic,
                        Operands = instr.Operand,
                        Bytes = instr.Bytes,
                        Details = instr.Details // Присваиваем детали
                    });
                }
            }
            catch (DllNotFoundException dllEx)
            {
                Console.WriteLine($"[MethodDisassembler] Capstone native library not found. Ensure capstone.dll (or .so/.dylib) is present. Error: {dllEx.Message}");
                // Здесь можно было бы вернуть специальный флаг или кинуть кастомное исключение, чтобы основная логика поняла, что дизассемблирование не удалось.
                // Пока просто выводим в консоль и возвращаем пустой список.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MethodDisassembler] Error during disassembly for architecture {architecture}: {ex.Message}");
            }
            finally
            {
                disassembler?.Dispose();
            }

            return instructions;
        }
    }
}
