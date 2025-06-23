using Il2CppDumper.lifting;
using Il2CppDumper.lifting.Operation;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Il2CppDumper.UppdateLowCode
{
    internal class IROptimizer
    {
        public List<IROperation> Optimize(List<IROperation> operations,
                                         Dictionary<string, TypeReference> typeMap)
        {
            var optimized = new List<IROperation>();
            IROperation lastOp = null;

            foreach (var op in operations)
            {
                var processed = false;

                // Dead code elimination
                if (lastOp is ReturnOperation || lastOp is JumpOperation)
                {
                    if (!(op is JumpOperation jump && jump.TargetLabel.StartsWith("L_")))
                    {
                        continue;
                    }
                }

                // Constant folding
                if (op is BinaryOperation binOp)
                {
                    if (TryConstantFold(binOp, out var result))
                    {
                        optimized.Add(new AssignOperation
                        {
                            Address = binOp.Address,
                            Destination = binOp.Destination,
                            Source = result
                        });
                        processed = true;
                    }
                }

                // Copy propagation
                if (!processed && op is AssignOperation assign)
                {
                    if (lastOp is AssignOperation lastAssign &&
                        AreOperandsEqual(assign.Source, lastAssign.Destination))
                    {
                        if (AreOperandsEqual(assign.Destination, lastAssign.Source))
                        {
                            // Eliminate redundant swap
                            processed = true;
                        }
                        else
                        {
                            // Propagate value
                            assign.Source = lastAssign.Source;
                        }
                    }
                }

                if (!processed)
                {
                    optimized.Add(op);
                }

                lastOp = op;
            }

            return optimized;
        }

        private bool TryConstantFold(BinaryOperation binOp, out IROperand result)
        {
            result = null;

            if (binOp.Left is ConstantOperand leftConst &&
                binOp.Right is ConstantOperand rightConst)
            {
                try
                {
                    object value = binOp.OperationType switch
                    {
                        BinaryOperationType.Add => Convert.ToDouble(leftConst.Value) + Convert.ToDouble(rightConst.Value),
                        BinaryOperationType.Sub => Convert.ToDouble(leftConst.Value) - Convert.ToDouble(rightConst.Value),
                        BinaryOperationType.Mul => Convert.ToDouble(leftConst.Value) * Convert.ToDouble(rightConst.Value),
                        BinaryOperationType.Div => Convert.ToDouble(leftConst.Value) / Convert.ToDouble(rightConst.Value),
                        BinaryOperationType.And => Convert.ToInt64(leftConst.Value) & Convert.ToInt64(rightConst.Value),
                        BinaryOperationType.Or => Convert.ToInt64(leftConst.Value) | Convert.ToInt64(rightConst.Value),
                        BinaryOperationType.Xor => Convert.ToInt64(leftConst.Value) ^ Convert.ToInt64(rightConst.Value),
                        _ => null
                    };

                    if (value != null)
                    {
                        result = new ConstantOperand
                        {
                            Value = value,
                            Type = binOp.Left.Type
                        };
                        return true;
                    }
                }
                catch
                {
                    // Ignore conversion errors
                }
            }

            return false;
        }

        private bool AreOperandsEqual(IROperand op1, IROperand op2)
        {
            if (op1.GetType() != op2.GetType()) return false;

            return op1 switch
            {
                RegisterOperand reg1 when op2 is RegisterOperand reg2 =>
                    reg1.Name == reg2.Name,
                ConstantOperand const1 when op2 is ConstantOperand const2 =>
                    Equals(const1.Value, const2.Value),
                _ => false
            };
        }
    }
}
