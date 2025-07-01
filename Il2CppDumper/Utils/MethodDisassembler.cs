using System;
using System.Collections.Generic;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Gee.External.Capstone.X86;

namespace Il2CppDumper
{
    public class DisassembledInstruction
    {
        public ulong Address { get; set; }
        public short Size { get; set; }
        public string Mnemonic { get; set; }
        public string Operands { get; set; }
        public byte[] Bytes { get; set; }
        public object PlatformSpecificInstruction { get; set; }

        public override string ToString()
        {
            return $"0x{Address:X}: {Mnemonic} {Operands}";
        }
    }

    public static class MethodDisassembler
    {
        public static List<DisassembledInstruction> Disassemble(byte[] codeBytes, ulong baseAddress, ArchitectureType architecture)
        {
            if (codeBytes == null) throw new ArgumentNullException(nameof(codeBytes));
            if (codeBytes.Length == 0)
            {
                Console.WriteLine("[MethodDisassembler] Empty codeBytes provided");
                return new List<DisassembledInstruction>();
            }
            if (baseAddress == 0)
            {
                Console.WriteLine("[MethodDisassembler] Invalid base address");
                return new List<DisassembledInstruction>();
            }
            var instructionsResult = new List<DisassembledInstruction>();
            if (codeBytes == null || codeBytes.Length == 0) return instructionsResult;

            CapstoneDisassembler disassembler = null;
            try
            {
                switch (architecture)
                {
                    case ArchitectureType.X86_64:
                        var x64Disassembler = CapstoneDisassembler.CreateX86Disassembler(X86DisassembleMode.Bit64);
                        x64Disassembler.EnableInstructionDetails = true;
                        var x64Instructions = x64Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        AddX86Instructions(x64Instructions, instructionsResult);
                        disassembler = x64Disassembler;
                        break;
                    case ArchitectureType.X86_32:
                        var x86Disassembler = CapstoneDisassembler.CreateX86Disassembler(X86DisassembleMode.Bit32);
                        x86Disassembler.EnableInstructionDetails = true;
                        var x86Instructions = x86Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        AddX86Instructions(x86Instructions, instructionsResult);
                        disassembler = x86Disassembler;
                        break;
                    case ArchitectureType.ARM64:
                        var arm64Disassembler = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.Arm);
                        arm64Disassembler.EnableInstructionDetails = true;
                        var arm64Instructions = arm64Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        AddArm64Instructions(arm64Instructions, instructionsResult);
                        disassembler = arm64Disassembler;
                        break;
                    case ArchitectureType.ARM32:
                        var arm32Disassembler = CapstoneDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
                        arm32Disassembler.EnableInstructionDetails = true;
                        var arm32Instructions = arm32Disassembler.Disassemble(codeBytes, (long)baseAddress);
                        AddArmInstructions(arm32Instructions, instructionsResult);
                        disassembler = arm32Disassembler;
                        break;
                    default:
                        Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Unsupported or Unknown architecture: {architecture}");
                        return instructionsResult;
                }
            }
            catch (DllNotFoundException dllEx)
            {
                Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Native library not found. Error: {dllEx.Message}");
            }
            catch (CapstoneException capEx)
            {
                Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: Capstone error for arch {architecture}: {capEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MethodDisassembler] Gee.External.Capstone: General error for arch {architecture}: {ex.Message}");
            }
            finally
            {
                disassembler?.Dispose();
            }

            return instructionsResult;
        }

        private static void AddX86Instructions(IEnumerable<X86Instruction> instructions, List<DisassembledInstruction> result)
        {
            foreach (var instr in instructions)
            {
                result.Add(new DisassembledInstruction
                {
                    Address = (ulong)instr.Address,
                    Size = (short)instr.Bytes.Length,
                    Mnemonic = instr.Mnemonic,
                    Operands = instr.Operand,
                    Bytes = instr.Bytes,
                    PlatformSpecificInstruction = instr
                });
            }
        }

        private static void AddArm64Instructions(IEnumerable<Arm64Instruction> instructions, List<DisassembledInstruction> result)
        {
            foreach (var instr in instructions)
            {
                result.Add(new DisassembledInstruction
                {
                    Address = (ulong)instr.Address,
                    Size = (short)instr.Bytes.Length,
                    Mnemonic = instr.Mnemonic,
                    Operands = instr.Operand,
                    Bytes = instr.Bytes,
                    PlatformSpecificInstruction = instr
                });
            }
        }

        private static void AddArmInstructions(IEnumerable<ArmInstruction> instructions, List<DisassembledInstruction> result)
        {
            foreach (var instr in instructions)
            {
                result.Add(new DisassembledInstruction
                {
                    Address = (ulong)instr.Address,
                    Size = (short)instr.Bytes.Length,
                    Mnemonic = instr.Mnemonic,
                    Operands = instr.Operand,
                    Bytes = instr.Bytes,
                    PlatformSpecificInstruction = instr
                });
            }
        }
    }
}