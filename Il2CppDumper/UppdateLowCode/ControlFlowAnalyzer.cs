using Il2CppDumper.lifting.Operation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Il2CppDumper.UppdateLowCode
{
    internal class ControlFlowAnalyzer
    {
        public List<IRBlock> StructureBlocks(List<IROperation> operations)
        {
            var blocks = new List<IRBlock>();
            var blockMap = new Dictionary<ulong, IRBlock>();
            var leaders = new HashSet<ulong>();
            var blockStarts = new Dictionary<ulong, IRBlock>();

            // Step 1: Identify leaders
            leaders.Add(0); // First instruction is always a leader

            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];

                if (op is JumpOperation jump)
                {
                    leaders.Add(GetLabelAddress(jump.TargetLabel));
                    if (i + 1 < operations.Count)
                    {
                        leaders.Add((ulong)operations[i + 1].Address);
                    }
                }
                else if (op is ConditionalJumpOperation condJump)
                {
                    leaders.Add(GetLabelAddress(condJump.TargetLabel));
                    if (i + 1 < operations.Count)
                    {
                        leaders.Add((ulong)operations[i + 1].Address);
                    }
                }
                else if (op is ReturnOperation)
                {
                    if (i + 1 < operations.Count)
                    {
                        leaders.Add((ulong)operations[i + 1].Address);
                    }
                }
            }

            // Step 2: Create basic blocks
            IRBlock currentBlock = null;
            foreach (var op in operations)
            {
                if (leaders.Contains((ulong)op.Address))
                {
                    currentBlock = new IRBlock
                    {
                        StartAddress = (ulong)op.Address,
                        Operations = new List<IROperation>()
                    };
                    blocks.Add(currentBlock);
                    blockStarts[(ulong)op.Address] = currentBlock;
                }

                if (currentBlock != null)
                {
                    currentBlock.Operations.Add(op);

                    // Check for block terminators
                    if (op is JumpOperation || op is ConditionalJumpOperation || op is ReturnOperation)
                    {
                        currentBlock = null;
                    }
                }
            }

            // Step 3: Build control flow graph
            foreach (var block in blocks)
            {
                var lastOp = block.Operations.LastOrDefault();
                if (lastOp is JumpOperation jump)
                {
                    var targetAddr = GetLabelAddress(jump.TargetLabel);
                    if (blockStarts.TryGetValue(targetAddr, out var targetBlock))
                    {
                        block.Successors.Add(targetBlock);
                        targetBlock.Predecessors.Add(block);
                    }
                }
                else if (lastOp is ConditionalJumpOperation condJump)
                {
                    // True branch
                    var targetAddr = GetLabelAddress(condJump.TargetLabel);
                    if (blockStarts.TryGetValue(targetAddr, out var trueBlock))
                    {
                        block.Successors.Add(trueBlock);
                        trueBlock.Predecessors.Add(block);
                    }

                    // False branch (next instruction)
                    var nextIndex = blocks.IndexOf(block) + 1;
                    if (nextIndex < blocks.Count)
                    {
                        var falseBlock = blocks[nextIndex];
                        block.Successors.Add(falseBlock);
                        falseBlock.Predecessors.Add(block);
                    }
                }
                else if (!(lastOp is ReturnOperation))
                {
                    // Fall-through to next block
                    var nextIndex = blocks.IndexOf(block) + 1;
                    if (nextIndex < blocks.Count)
                    {
                        var nextBlock = blocks[nextIndex];
                        block.Successors.Add(nextBlock);
                        nextBlock.Predecessors.Add(block);
                    }
                }
            }

            return blocks;
        }

        private ulong GetLabelAddress(string label)
        {
            if (label.StartsWith("L_"))
            {
                var hexPart = label.Substring(2);
                if (ulong.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var address))
                {
                    return address;
                }
            }
            return 0;
        }
    }

    internal class IRBlock
    {
        public ulong StartAddress { get; set; }
        public List<IROperation> Operations { get; set; } = new List<IROperation>();
        public List<IRBlock> Predecessors { get; set; } = new List<IRBlock>();
        public List<IRBlock> Successors { get; set; } = new List<IRBlock>();
        public bool Visited { get; set; }

        public bool IsLoopHeader { get; set; }
        public IRBlock LoopEnd { get; set; }
    }
}
