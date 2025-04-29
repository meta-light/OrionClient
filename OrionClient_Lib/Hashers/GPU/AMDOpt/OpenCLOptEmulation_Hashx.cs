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

namespace OrionClientLib.Hashers.GPU.AMDBaseline
{
    public partial class OpenCLOptEmulationGPUHasher
    {
        private static int _offsetCounter = 0;
        public const int HashxBlockSize = 128;
        private const int _blockSize = HashxBlockSize;

        public static void Hashx(ArrayView<Instruction> program, ArrayView<SipState> key, ArrayView<ulong> results)
        {
            var grid = Grid.GlobalIndex;
            var group = Group.Dimension;

            int index = (grid.X * group.Y + grid.Y);// % (ushort.MaxValue + 1);

            //var sMemory = SharedMemory.Allocate<Instruction>(512);
            var registers = SharedMemory.Allocate<ulong>(8 * _blockSize);
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
                    V0 = registers[0 * _blockSize + idx] + key.V0,
                    V1 = registers[1 * _blockSize + idx] + key.V1,
                    V2 = registers[2 * _blockSize + idx],
                    V3 = registers[3 * _blockSize + idx]
                };

                x.SipRound();

                SipState y = new SipState
                {
                    V0 = registers[4 * _blockSize + idx],
                    V1 = registers[5 * _blockSize + idx],
                    V2 = registers[6 * _blockSize + idx] + key.V2,
                    V3 = registers[7 * _blockSize + idx] + key.V3
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
                var multInstruction_1 = LoadMultInstruction(ref startInstruction, 0 * 4);
                LoadTargetInstruction();
                var multInstruction_preTarget = LoadMultInstruction(ref startInstruction, 2 * 4);

                Store(idx, registers, multInstruction_1.Dst, LoadRegister(idx, registers, multInstruction_1.Src) * LoadRegister(idx, registers, multInstruction_1.Dst));

                //Warp.Barrier();

                //Got a lot of extra registers
                var basicInstruction_preTarget = LoadBasicInstruction(ref startInstruction, 3 * 4);
                var basicInstruction_preTarget2 = LoadBasicInstruction(ref startInstruction, 4 * 4);
                var multInstruction_preTarget2 = LoadMultInstruction(ref startInstruction, 5 * 4);
                var basicInstruction_preTarget3 = LoadBasicInstruction(ref startInstruction, 6 * 4);
                var basicInstruction_preTarget4 = LoadBasicInstruction(ref startInstruction, 7 * 4);
                var multInstruction_preTarget3 = LoadMultInstruction(ref startInstruction, 8 * 4);
                var basicInstruction_preTarget5 = LoadBasicInstruction(ref startInstruction, 9 * 4);
                var basicInstruction_preTarget6 = LoadBasicInstruction(ref startInstruction, 10 * 4);
                var highMulInstruction_preTarget1 = LoadBasicInstruction(ref startInstruction, 11 * 4);
                var basicInstruction_preTarget7 = LoadBasicInstruction(ref startInstruction, 12 * 4);
                var multInstruction_preTarget4 = LoadMultInstruction(ref startInstruction, 13 * 4);
                var basicInstruction_preTarget8 = LoadBasicInstruction(ref startInstruction, 14 * 4);
                var basicInstruction_preTarget9 = LoadBasicInstruction(ref startInstruction, 15 * 4);
                var multInstruction_preTarget5 = LoadMultInstruction(ref startInstruction, 16 * 4);
                var branchInstruction = LoadBranchInstruction(ref startInstruction, 17 * 4);

            target:

                //Multiply
                Store(idx, registers, multInstruction_preTarget.Dst, LoadRegister(idx, registers, multInstruction_preTarget.Src) * LoadRegister(idx, registers, multInstruction_preTarget.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_preTarget.Dst, BasicOperation(idx, basicInstruction_preTarget.Type, basicInstruction_preTarget.Dst, basicInstruction_preTarget.Src, basicInstruction_preTarget.Operand, registers));
                //Mult instruction 2 here

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
                var multInstruction_aftTarget1 = LoadMultInstruction(ref startInstruction, 18 * 4);
                var basicInstruction_aftTarget1 = LoadBasicInstruction(ref startInstruction, 19 * 4);
                var basicInstruction_aftTarget2 = LoadBasicInstruction(ref startInstruction, 20 * 4);

                Store(idx, registers, multInstruction_aftTarget1.Dst, LoadRegister(idx, registers, multInstruction_aftTarget1.Src) * LoadRegister(idx, registers, multInstruction_aftTarget1.Dst));

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget1.Dst, BasicOperation(idx, basicInstruction_aftTarget1.Type, basicInstruction_aftTarget1.Dst, basicInstruction_aftTarget1.Src, basicInstruction_aftTarget1.Operand, registers));

                var highMulInstruction = LoadBasicInstruction(ref startInstruction, 21 * 4);
                var basicInstruction_aftTarget3 = LoadBasicInstruction(ref startInstruction, 22 * 4);
                var multInstruction_aftTarget2 = LoadMultInstruction(ref startInstruction, 23 * 4);

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget2.Dst, BasicOperation(idx, basicInstruction_aftTarget2.Type, basicInstruction_aftTarget2.Dst, basicInstruction_aftTarget2.Src, basicInstruction_aftTarget2.Operand, registers));

                #region High Multiply

                mulA = LoadRegister(idx, registers, highMulInstruction.Dst);
                mulB = LoadRegister(idx, registers, highMulInstruction.Src);

                mulHi = highMulInstruction.Type == (int)OpCode.UMulH ? Mul64hi(mulA, mulB) : (ulong)Mul64hi((long)mulA, (long)mulB);

                Store(idx, registers, highMulInstruction.Dst, mulHi);

                #endregion


                var basicInstruction_aftTarget4 = LoadBasicInstruction(ref startInstruction, 24 * 4);
                var basicInstruction_aftTarget5 = LoadBasicInstruction(ref startInstruction, 25 * 4);


                //Basic opt
                Store(idx, registers, basicInstruction_aftTarget3.Dst, BasicOperation(idx, basicInstruction_aftTarget3.Type, basicInstruction_aftTarget3.Dst, basicInstruction_aftTarget3.Src, basicInstruction_aftTarget3.Operand, registers));

                //Multiply
                Store(idx, registers, multInstruction_aftTarget2.Dst, LoadRegister(idx, registers, multInstruction_aftTarget2.Src) * LoadRegister(idx, registers, multInstruction_aftTarget2.Dst));


                var multInstruction_aftTarget3 = LoadMultInstruction(ref startInstruction, 26 * 4);
                var basicInstruction_aftTarget6 = LoadBasicInstruction(ref startInstruction, 27 * 4);

                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget4.Dst, BasicOperation(idx, basicInstruction_aftTarget4.Type, basicInstruction_aftTarget4.Dst, basicInstruction_aftTarget4.Src, basicInstruction_aftTarget4.Operand, registers));


                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget5.Dst, BasicOperation(idx, basicInstruction_aftTarget5.Type, basicInstruction_aftTarget5.Dst, basicInstruction_aftTarget5.Src, basicInstruction_aftTarget5.Operand, registers));


                var basicInstruction_aftTarget7 = LoadBasicInstruction(ref startInstruction, 28 * 4);
                var multInstruction_aftTarget4 = LoadMultInstruction(ref startInstruction, 29 * 4);

                //Multiply
                Store(idx, registers, multInstruction_aftTarget3.Dst, LoadRegister(idx, registers, multInstruction_aftTarget3.Src) * LoadRegister(idx, registers, multInstruction_aftTarget3.Dst));


                //Basic Opt
                Store(idx, registers, basicInstruction_aftTarget6.Dst, BasicOperation(idx, basicInstruction_aftTarget6.Type, basicInstruction_aftTarget6.Dst, basicInstruction_aftTarget6.Src, basicInstruction_aftTarget6.Operand, registers));

                var basicInstruction_aftTarget8 = LoadBasicInstruction(ref startInstruction, 30 * 4);
                var basicInstruction_aftTarget9 = LoadBasicInstruction(ref startInstruction, 31 * 4);

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

                if (type != (int)OpCode.Rotate)
                {
                    ulong src = LoadRegister(idx, registers, srcId);

                    if (type == (int)OpCode.Xor)
                    {
                        return (dst) ^ (src);
                    }

                    return Mad(dst, src, (ulong)operand);
                }


                return (dst << (64 - operand)) ^ (dst >> operand);
            }

            if(type == (int)OpCode.XorConst)
            {
                return dst ^ (ulong)operand;
            }
            
            return dst + (ulong)operand;
        }

        #endregion

        #region Loading Instruction

        [IntrinsicMethod(nameof(LoadInstruction_Generate))]
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void LoadTargetInstruction()
        {
        }

        private static void LoadTargetInstruction_Generate(PTXBackend backend, PTXCodeGenerator codeGenerator, Value value)
        {
            _offsetCounter += 16;
        }

        [IntrinsicMethod(nameof(LoadHighMultInstruction_Generate))]
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
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
        //[IntrinsicImplementation]
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

        private static unsafe void Store(int idx, ArrayView<ulong> registers, int id, ulong value)
        {
            registers[id * HashxBlockSize + idx] = value;
        }

        #endregion
    }
}
