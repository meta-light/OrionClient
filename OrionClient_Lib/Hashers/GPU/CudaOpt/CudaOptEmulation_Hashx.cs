using DrillX.Compiler;
using DrillX;
using DrillX.Solver;
using ILGPU;
using ILGPU.Backends.PTX;
using ILGPU.Backends;
using ILGPU.IR.Intrinsics;
using ILGPU.IR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ILGPU.Runtime.Cuda;

namespace OrionClientLib.Hashers.GPU.Baseline
{
    public partial class CudaOptEmulationGPUHasher
    {
        private static int _offsetCounter = 0;
        public const int HashxBlockSize = 128;

        private static void Hashx(ArrayView<Instruction> program, ArrayView<SipState> key, ArrayView<ulong> results)
        {
            var grid = Grid.GlobalIndex;
            var group = Group.Dimension;

            int index = (grid.X * group.Y + grid.Y);// % (ushort.MaxValue + 1);

            //var sMemory = SharedMemory.Allocate<Instruction>(512);
            var registers = SharedMemory.Allocate<ulong>(8 * HashxBlockSize);
            var idx = Group.IdxX;

            //Interop.WriteLine("{0}", idx);

            //var bInstruction = program.SubView(index / (ushort.MaxValue + 1) * 512).Cast<ulong>();
            //var uMemory = sMemory.Cast<ulong>();

            //for (int i = idx; i < 1024; i += Group.DimX)
            //{
            //    uMemory[i] = bInstruction[i];
            //}

            //Group.Barrier();

            var sharedProgram = SharedMemory.Allocate<int>(Instruction.ProgramSize).Cast<ulong>();
            var p = program.Cast<int>().SubView(index / (ushort.MaxValue + 1) * Instruction.ProgramSize, Instruction.ProgramSize).Cast<ulong>();

            for (int i = idx; i < Instruction.ProgramSize / 2; i += Group.DimX)
            {
                sharedProgram[i] = p[i];
            }

            Group.Barrier();

            results[index] = Emulate(sharedProgram.Cast<int>(), key.SubView(index / (ushort.MaxValue + 1)), (ulong)(index % (ushort.MaxValue + 1)), registers, idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Emulate(ArrayView<int> program, ArrayView<SipState> key, ulong input, ArrayView<ulong> sRegs, int idx)
        {
            //return InterpretFull(ref program[0], ref key[0], input);

            return Interpret(program, key[0], input, sRegs, idx);
            //return InterpetCompiled(key.V0, key.V1, key.V2, key.V3, input); 
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Digest(int idx, ArrayView<ulong> registers, SipState key)
        {
            unchecked
            {
                SipState x = new SipState
                {
                    V0 = registers[0 * HashxBlockSize + idx] + key.V0,
                    V1 = registers[1 * HashxBlockSize + idx] + key.V1,
                    V2 = registers[2 * HashxBlockSize + idx],
                    V3 = registers[3 * HashxBlockSize + idx]
                };

                x.SipRound();

                SipState y = new SipState
                {
                    V0 = registers[4 * HashxBlockSize + idx],
                    V1 = registers[5 * HashxBlockSize + idx],
                    V2 = registers[6 * HashxBlockSize + idx] + key.V2,
                    V3 = registers[7 * HashxBlockSize + idx] + key.V3
                };

                y.SipRound();

                return x.V0 ^ y.V0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Interpret(ArrayView<int> program, SipState key, ulong input, ArrayView<ulong> registers, int idx)
        {
            registers = SipHash24Ctr(idx, key, input, registers);
            bool allowBranch = true;

            
            for (int i = 0; i < 16; i++)
            {
                //ArrayView<int> startInstruction = program.SubView(i * Instruction.Size, Instruction.Size);
                ref int startInstruction = ref program.SubView(i * Instruction.Size, Instruction.Size)[0];


                //Multiply
                var multInstruction_1 = LoadMultInstruction(ref startInstruction, 0);
                LoadTargetInstruction();
                var multInstruction_preTarget = LoadMultInstruction(ref startInstruction, MultIntruction.Size * 1 + BasicInstruction.Size * 0);

                Store(idx, registers, multInstruction_1.Dst, LoadRegister(idx, registers, multInstruction_1.Src) * LoadRegister(idx, registers, multInstruction_1.Dst));

                //Warp.Barrier();

                //Got a lot of extra registers
                var basicInstruction_preTarget = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 2 + BasicInstruction.Size * 0);
                var basicInstruction_preTarget2 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 2 + BasicInstruction.Size * 1);
                var multInstruction_preTarget2 = LoadMultInstruction(ref startInstruction, MultIntruction.Size * 2 + BasicInstruction.Size * 2);
                var basicInstruction_preTarget3 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 3 + BasicInstruction.Size * 2);
                var basicInstruction_preTarget4 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 3 + BasicInstruction.Size * 3);
                var multInstruction_preTarget3 = LoadMultInstruction(ref startInstruction, MultIntruction.Size * 3 + BasicInstruction.Size * 4);
                var basicInstruction_preTarget5 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 4 + BasicInstruction.Size * 4);
                var basicInstruction_preTarget6 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 4 + BasicInstruction.Size * 5);
                var highMulInstruction_preTarget1 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 4 + HiMultInstruction.Size * 0 + BasicInstruction.Size * 6);
                var basicInstruction_preTarget7 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 4 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 6);
                var multInstruction_preTarget4 = LoadMultInstruction(ref startInstruction, MultIntruction.Size * 4 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 7);

            target:

                //Multiply
                Store(idx, registers, multInstruction_preTarget.Dst, LoadRegister(idx, registers, multInstruction_preTarget.Src) * LoadRegister(idx, registers, multInstruction_preTarget.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget.Dst, BasicOperation(idx, basicInstruction_preTarget.Type, basicInstruction_preTarget.Dst, basicInstruction_preTarget.Src, basicInstruction_preTarget.Operand, registers));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget2.Dst, BasicOperation(idx, basicInstruction_preTarget2.Type, basicInstruction_preTarget2.Dst, basicInstruction_preTarget2.Src, basicInstruction_preTarget2.Operand, registers));

                //Multiply
                Store(idx, registers, multInstruction_preTarget2.Dst, LoadRegister(idx, registers, multInstruction_preTarget2.Src) * LoadRegister(idx, registers, multInstruction_preTarget2.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget3.Dst, BasicOperation(idx, basicInstruction_preTarget3.Type, basicInstruction_preTarget3.Dst, basicInstruction_preTarget3.Src, basicInstruction_preTarget3.Operand, registers));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget4.Dst, BasicOperation(idx, basicInstruction_preTarget4.Type, basicInstruction_preTarget4.Dst, basicInstruction_preTarget4.Src, basicInstruction_preTarget4.Operand, registers));

                //Multiply
                Store(idx, registers, multInstruction_preTarget3.Dst, LoadRegister(idx, registers, multInstruction_preTarget3.Src) * LoadRegister(idx, registers, multInstruction_preTarget3.Dst));

                var basicInstruction_preTarget8 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 5 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 7);
                var basicInstruction_preTarget9 = LoadBasicInstruction(ref startInstruction, MultIntruction.Size * 5 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 8);
                var multInstruction_preTarget5 = LoadMultInstruction(ref startInstruction, MultIntruction.Size * 5 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 9);
                var branchInstruction = LoadBranchInstruction(ref startInstruction, MultIntruction.Size * 6 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 9);

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget5.Dst, BasicOperation(idx, basicInstruction_preTarget5.Type, basicInstruction_preTarget5.Dst, basicInstruction_preTarget5.Src, basicInstruction_preTarget5.Operand, registers));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget6.Dst, BasicOperation(idx, basicInstruction_preTarget6.Type, basicInstruction_preTarget6.Dst, basicInstruction_preTarget6.Src, basicInstruction_preTarget6.Operand, registers));

                #region High Multiply


                uint mulhResult;
                ulong mulHi;

                var mulA = LoadRegister(idx, registers, highMulInstruction_preTarget1.Dst);
                var mulB = LoadRegister(idx, registers, highMulInstruction_preTarget1.Src);

                mulHi = highMulInstruction_preTarget1.Type == (int)OpCode.UMulH ? Mul64hi(mulA, mulB) : (ulong)Mul64hi((long)mulA, (long)mulB);

                Store(idx, registers, highMulInstruction_preTarget1.Dst, mulHi);
                mulhResult = (uint)mulHi;
                #endregion

                //Basic opt
                Store(idx, registers, basicInstruction_preTarget7.Dst, BasicOperation(idx, basicInstruction_preTarget7.Type, basicInstruction_preTarget7.Dst, basicInstruction_preTarget7.Src, basicInstruction_preTarget7.Operand, registers));


                //Multiply
                Store(idx, registers, multInstruction_preTarget4.Dst, LoadRegister(idx, registers, multInstruction_preTarget4.Src) * LoadRegister(idx, registers, multInstruction_preTarget4.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget8.Dst, BasicOperation(idx, basicInstruction_preTarget8.Type, basicInstruction_preTarget8.Dst, basicInstruction_preTarget8.Src, basicInstruction_preTarget8.Operand, registers));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget9.Dst, BasicOperation(idx, basicInstruction_preTarget9.Type, basicInstruction_preTarget9.Dst, basicInstruction_preTarget9.Src, basicInstruction_preTarget9.Operand, registers));

                //Multiply
                Store(idx, registers, multInstruction_preTarget5.Dst, LoadRegister(idx, registers, multInstruction_preTarget5.Src) * LoadRegister(idx, registers, multInstruction_preTarget5.Dst));

                //Branch
                if (allowBranch && ((uint)branchInstruction.Mask & mulhResult) == 0)
                {
                    allowBranch = false;

                    goto target;
                }

                //Warp.Barrier();

                //Multiply
                var multInstruction_aftTarget1 = LoadMultInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 6 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 9);
                var basicInstruction_aftTarget1 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 7 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 9);
                var basicInstruction_aftTarget2 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 7 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 10);

                Store(idx, registers, multInstruction_aftTarget1.Dst, LoadRegister(idx, registers, multInstruction_aftTarget1.Src) * LoadRegister(idx, registers, multInstruction_aftTarget1.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget1.Dst, BasicOperation(idx, basicInstruction_aftTarget1.Type, basicInstruction_aftTarget1.Dst, basicInstruction_aftTarget1.Src, basicInstruction_aftTarget1.Operand, registers));
                
                var highMulInstruction = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 7 + HiMultInstruction.Size * 1 + BasicInstruction.Size * 11);
                var basicInstruction_aftTarget3 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 7 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 11);
                var multInstruction_aftTarget2 = LoadMultInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 7 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 12);

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget2.Dst, BasicOperation(idx, basicInstruction_aftTarget2.Type, basicInstruction_aftTarget2.Dst, basicInstruction_aftTarget2.Src, basicInstruction_aftTarget2.Operand, registers));

                #region High Multiply

                mulA = LoadRegister(idx, registers, highMulInstruction.Dst);
                mulB = LoadRegister(idx, registers, highMulInstruction.Src);

                mulHi = highMulInstruction.Type == (int)OpCode.UMulH ? Mul64hi(mulA, mulB) : (ulong)Mul64hi((long)mulA, (long)mulB);

                Store(idx, registers, highMulInstruction.Dst, mulHi);

                #endregion


                var basicInstruction_aftTarget4 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 8 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 12);
                var basicInstruction_aftTarget5 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 8 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 13);


                //Basic opt
                Store(idx, registers, basicInstruction_aftTarget3.Dst, BasicOperation(idx, basicInstruction_aftTarget3.Type, basicInstruction_aftTarget3.Dst, basicInstruction_aftTarget3.Src, basicInstruction_aftTarget3.Operand, registers));

                //Multiply
                Store(idx, registers, multInstruction_aftTarget2.Dst, LoadRegister(idx, registers, multInstruction_aftTarget2.Src) * LoadRegister(idx, registers, multInstruction_aftTarget2.Dst));


                var multInstruction_aftTarget3 = LoadMultInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 8 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 14);
                var basicInstruction_aftTarget6 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 9 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 14);

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget4.Dst, BasicOperation(idx, basicInstruction_aftTarget4.Type, basicInstruction_aftTarget4.Dst, basicInstruction_aftTarget4.Src, basicInstruction_aftTarget4.Operand, registers));


                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget5.Dst, BasicOperation(idx, basicInstruction_aftTarget5.Type, basicInstruction_aftTarget5.Dst, basicInstruction_aftTarget5.Src, basicInstruction_aftTarget5.Operand, registers));


                var basicInstruction_aftTarget7 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 9 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 15);
                var multInstruction_aftTarget4 = LoadMultInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 9 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 16);

                //Multiply
                Store(idx, registers, multInstruction_aftTarget3.Dst, LoadRegister(idx, registers, multInstruction_aftTarget3.Src) * LoadRegister(idx, registers, multInstruction_aftTarget3.Dst));


                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget6.Dst, BasicOperation(idx, basicInstruction_aftTarget6.Type, basicInstruction_aftTarget6.Dst, basicInstruction_aftTarget6.Src, basicInstruction_aftTarget6.Operand, registers));

                var basicInstruction_aftTarget8 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 10 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 16);
                var basicInstruction_aftTarget9 = LoadBasicInstruction(ref startInstruction, BranchInstruction.Size + MultIntruction.Size * 10 + HiMultInstruction.Size * 2 + BasicInstruction.Size * 17);

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget7.Dst, BasicOperation(idx, basicInstruction_aftTarget7.Type, basicInstruction_aftTarget7.Dst, basicInstruction_aftTarget7.Src, basicInstruction_aftTarget7.Operand, registers));


                //Multiply
                Store(idx, registers, multInstruction_aftTarget4.Dst, LoadRegister(idx, registers, multInstruction_aftTarget4.Src) * LoadRegister(idx, registers, multInstruction_aftTarget4.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget8.Dst, BasicOperation(idx, basicInstruction_aftTarget8.Type, basicInstruction_aftTarget8.Dst, basicInstruction_aftTarget8.Src, basicInstruction_aftTarget8.Operand, registers));

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget9.Dst, BasicOperation(idx, basicInstruction_aftTarget9.Type, basicInstruction_aftTarget9.Dst, basicInstruction_aftTarget9.Src, basicInstruction_aftTarget9.Operand, registers));
            }

            return Digest(idx, registers, key);
        }

        #region Basic Operation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong BasicOperation(int idx, int type, int dstId, int srcId, int operand, ArrayView<ulong> registers)
        {
            ulong dst = LoadRegister(idx, registers, dstId);

            if (type <= (int)OpCode.Rotate)
            {
                ulong src = LoadRegister(idx, registers, srcId);

                if (type != (int)OpCode.AddShift)
                {
                    return (dst << (64 - operand)) ^ (src >> operand);
                }

                return Mad(dst, src, (ulong)operand);
            }

            var a = dst ^ (ulong)operand;
            var b = dst + (ulong)operand;

            return BitwiseSelectPTX(type == (int)OpCode.XorConst, a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [IntrinsicMethod(nameof(BitwiseSelectPTX_Generate))]
        [IntrinsicImplementation]
        private static ulong BitwiseSelectPTX(bool cond, ulong a, ulong b)
        {
            return 0;
        }


        private static void BitwiseSelectPTX_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var cond = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);
            var a = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[1]);
            var b = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[2]);
            var ret = codeGenerator.AllocateHardware(value);

            var command = codeGenerator.BeginCommand($"selp.u64 %{PTXRegisterAllocator.GetStringRepresentation(ret)}, %{PTXRegisterAllocator.GetStringRepresentation(a)}, %{PTXRegisterAllocator.GetStringRepresentation(b)},  %{PTXRegisterAllocator.GetStringRepresentation(cond)}");
            command.Dispose();
        }

        #endregion

        #region Loading Instruction

        [IntrinsicMethod(nameof(LoadInstruction_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BasicInstruction LoadBasicInstruction(ref int startInstruction, int index)
        {
            fixed (int* ptr = &startInstruction)
            {
                return ((BasicInstruction*)(ptr + index))[0];
            }
        }

        private static void LoadInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var instructionRef = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);

            var returnValue = (RegisterAllocator<PTXRegisterKind>.CompoundRegister)codeGenerator.Allocate(value);
            var command = codeGenerator.BeginCommand($"ld.shared.v4.s32 {{" +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[0])}," +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[1])}," +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[2])}," +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[3])}" +
                $"}}, " +
                $"[%{PTXRegisterAllocator.GetStringRepresentation(instructionRef)}+{_offsetCounter}]");
            command.Dispose();

            _offsetCounter += 16;
            _offsetCounter %= (32 * 16);
        }


        [IntrinsicMethod(nameof(LoadBranchInstruction_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BranchInstruction LoadBranchInstruction(ref int startInstruction, int index)
        {
            fixed (int* ptr = &startInstruction)
            {
                return ((BranchInstruction*)(ptr + index))[0];
            }
        }

        private static void LoadBranchInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var instructionRef = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);

            var returnValue = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.Allocate(value);
            var command = codeGenerator.BeginCommand($"ld.shared.s32 %{PTXRegisterAllocator.GetStringRepresentation(returnValue)}, [%{PTXRegisterAllocator.GetStringRepresentation(instructionRef)}+{_offsetCounter}]");
            command.Dispose();

            _offsetCounter += 16;
        }

        [IntrinsicMethod(nameof(LoadMultInstruction_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe MultIntruction LoadMultInstruction(ref int startInstruction, int index)
        {
            fixed (int* ptr = &startInstruction)
            {
                return ((MultIntruction*)(ptr + index))[0];
            }
        }

        private static void LoadMultInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var instructionRef = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);

            var returnValue = (RegisterAllocator<PTXRegisterKind>.CompoundRegister)codeGenerator.Allocate(value);
            var command = codeGenerator.BeginCommand($"ld.shared.v2.s32 {{" +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[0])}," +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[1])}" +
                $"}}, " +
                $"[%{PTXRegisterAllocator.GetStringRepresentation(instructionRef)}+{_offsetCounter}]");
            command.Dispose();

            _offsetCounter += 16;
        }

        [IntrinsicMethod(nameof(LoadTargetInstruction_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void LoadTargetInstruction()
        {
        }

        private static void LoadTargetInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            _offsetCounter += 16;
        }

        [IntrinsicMethod(nameof(LoadHighMultInstruction_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe HiMultInstruction LoadHighMultInstruction(ref int startInstruction, int index)
        {
            fixed (int* ptr = &startInstruction)
            {
                return ((HiMultInstruction*)(ptr + index))[0];
            }
        }

        private static void LoadHighMultInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var instructionRef = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);

            var returnValue = (RegisterAllocator<PTXRegisterKind>.CompoundRegister)codeGenerator.Allocate(value);
            var command = codeGenerator.BeginCommand($"ld.shared.v2.s32 {{" +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[0])}," +
                $"%{PTXRegisterAllocator.GetStringRepresentation((RegisterAllocator<PTXRegisterKind>.HardwareRegister)returnValue.Children[1])}" +
                $"}}, " +
                $"[%{PTXRegisterAllocator.GetStringRepresentation(instructionRef)}+{_offsetCounter}]");
            command.Dispose();

            _offsetCounter += 16;
        }

        #endregion

        #region Multiply Add

        [IntrinsicMethod(nameof(Mad_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mad(ulong a, ulong b, ulong operand)
        {
            return a + (b * operand);
        }

        private static void Mad_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var a = codeGenerator.LoadPrimitive(value[0]);
            var b = codeGenerator.LoadPrimitive(value[1]);
            var op = codeGenerator.LoadPrimitive(value[2]);
            var returnValue = codeGenerator.AllocateHardware(value);

            var command = codeGenerator.BeginCommand($"mad.lo.u64");
            command.AppendArgument(returnValue);
            command.AppendArgument(b);
            command.AppendArgument(op);
            command.AppendArgument(a);
            command.Dispose();
        }

        #endregion

        #region High Multiply 

        [IntrinsicMethod(nameof(Mulu64hi_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mul64hi(ulong a, ulong b)
        {
            uint num2 = (uint)a;
            uint num3 = (uint)(a >> 32);
            uint num4 = (uint)b;
            uint num5 = (uint)(b >> 32);
            ulong num6 = (ulong)num2 * (ulong)num4;
            ulong num7 = (ulong)((long)num3 * (long)num4) + (num6 >> 32);
            ulong num8 = (ulong)((long)num2 * (long)num5 + (uint)num7);
            return (ulong)((long)num3 * (long)num5 + (long)(num7 >> 32)) + (num8 >> 32);
        }

        [IntrinsicMethod(nameof(Muli64hi_Generate))]
        [IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long Mul64hi(long a, long b)
        {
            ulong num = Mul64hi((ulong)a, (ulong)b);
            return (long)num - ((a >> 63) & b) - ((b >> 63) & a);
        }

        private static void Mulu64hi_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var a = codeGenerator.LoadPrimitive(value[0]);
            var b = codeGenerator.LoadPrimitive(value[1]);
            var returnValue = codeGenerator.AllocateHardware(value);

            var command = codeGenerator.BeginCommand($"mul.hi.u64");
            command.AppendArgument(returnValue);
            command.AppendArgument(a);
            command.AppendArgument(b);
            command.Dispose();
        }

        private static void Muli64hi_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var a = codeGenerator.LoadPrimitive(value[0]);
            var b = codeGenerator.LoadPrimitive(value[1]);
            var returnValue = codeGenerator.AllocateHardware(value);

            var command = codeGenerator.BeginCommand($"mul.hi.s64");
            command.AppendArgument(returnValue);
            command.AppendArgument(a);
            command.AppendArgument(b);
            command.Dispose();
        }

        #endregion

        #region SipHash

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ArrayView<ulong> SipHash24Ctr(int idx, SipState s, ulong input, ArrayView<ulong> ret)
        {
            s.V1 ^= 0xee;
            s.V3 ^= input;

            s.SipRound();
            s.SipRound();

            s.V0 ^= input;
            s.V2 ^= 0xee;

            s.SipRound();
            s.SipRound();
            s.SipRound();
            s.SipRound();

            ret[0 * HashxBlockSize + idx] = s.V0;
            ret[1 * HashxBlockSize + idx] = s.V1;
            ret[2 * HashxBlockSize + idx] = s.V2;
            ret[3 * HashxBlockSize + idx] = s.V3;

            s.V1 ^= 0xdd;

            s.SipRound();
            s.SipRound();
            s.SipRound();
            s.SipRound();

            ret[4 * HashxBlockSize + idx] = s.V0;
            ret[5 * HashxBlockSize + idx] = s.V1;
            ret[6 * HashxBlockSize + idx] = s.V2;
            ret[7 * HashxBlockSize + idx] = s.V3;

            return ret;
        }

        #endregion

        #region Load Register

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong LoadRegister(int idx, ArrayView<ulong> registers, int id)
        {
            return registers[id * HashxBlockSize + idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LoadDualRegister(int idx, ArrayView<ulong> registers, int id, ref ulong ret)
        {
            if ((uint)id >= 8)
            {
                ret = (ulong)id;
                return;
            }

            ret = registers[id * HashxBlockSize + idx];
        }

        #endregion

        #region Store Register

        private static unsafe void Store(int idx, ArrayView<ulong> registers, int id, long value)
        {
            registers[id * HashxBlockSize + idx] = (ulong)value;
        }

        private static unsafe void Store(int idx, ArrayView<ulong> registers, int id, ulong value)
        {
            registers[id * HashxBlockSize + idx] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [IntrinsicMethod(nameof(Store_Generate))]
        [IntrinsicImplementation]
        private static unsafe void Store_Test(Registers* reg, int id, ulong value)
        {

        }

        private static void Store_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            var registers = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[0]);
            var id = (RegisterAllocator<PTXRegisterKind>.HardwareRegister)codeGenerator.LoadPrimitive(value[1]);
            var ret = codeGenerator.AllocateHardware(value);

            ////Set predicate
            //var command = codeGenerator.BeginCommand($"setp.ls.u64 %{PTXRegisterAllocator.GetStringRepresentation(b)}, %{PTXRegisterAllocator.GetStringRepresentation(idd)}, 64");
            //command.Dispose();

            ////Move id into ret
            //command = codeGenerator.BeginCommand($"mov.b64 %{PTXRegisterAllocator.GetStringRepresentation(ret)}, %{PTXRegisterAllocator.GetStringRepresentation(id)}");
            //command.Dispose();

            //Add
            var command = codeGenerator.BeginCommand($"add.u64 %{PTXRegisterAllocator.GetStringRepresentation(ret)}, %{PTXRegisterAllocator.GetStringRepresentation(id)}, %{PTXRegisterAllocator.GetStringRepresentation(registers)}");
            command.Dispose();

            //Load from register
            command = codeGenerator.BeginCommand($"ld.local.b64 %{PTXRegisterAllocator.GetStringRepresentation(ret)}, [%{PTXRegisterAllocator.GetStringRepresentation(ret)}]");
            command.Dispose();
        }

        #endregion

        #region Store Key

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void StoreValues(ref ulong arr, ulong a, ulong b, ulong c, ulong d)
        {
            unsafe
            {
                fixed (ulong* v = &arr)
                {
                    v[0] = a;
                    v[1] = b;
                    v[2] = c;
                    v[3] = d;
                }
            }
        }

        #endregion

        struct Registers
        {
            public ulong V0;
            public ulong V1;
            public ulong V2;
            public ulong V3;
            public ulong V4;
            public ulong V5;
            public ulong V6;
            public ulong V7;
        }
    }
}
