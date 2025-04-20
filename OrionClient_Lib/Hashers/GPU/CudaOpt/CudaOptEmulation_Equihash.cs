using DrillX.Compiler;
using DrillX;
using DrillX.Solver;
using ILGPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ILGPU.Backends.PTX;
using ILGPU.Backends;
using ILGPU.IR.Intrinsics;
using ILGPU.IR;
using System.Runtime.CompilerServices;

namespace OrionClientLib.Hashers.GPU.Baseline
{
    //This equihash implementation needs to be rewritten as currently 4% of solutions are invalid
    //Potential opts:
    //  - Split loops to take advantage of larger block sizes
    //  - Remove scratch usage
    public partial class CudaOptEmulationGPUHasher
    {
        public const int BlockSize = 512;
        public const int TotalValues = ushort.MaxValue + 1;

        public const long HeapSize = TotalValues * sizeof(ulong) * (4 + 3);
        const int numBuckets = 256;
        const int sharedBucketItems = 16; //8 are for a buffer
        const int totalSharedBucketItems = numBuckets * sharedBucketItems;
        const int passes = 32;
        const int halveMaxBucketItems = 2; //Halves the max bucket items

        /// <summary>
        /// 512 or 256
        /// </summary>
        const int maxBucketItems = 512; //512 or 256
        const int maxTotalBucketItems = maxBucketItems * numBuckets;

        const ulong mask60bit = (1ul << 60) - 1;

        private static void Equihash(ArrayView<ulong> values, ArrayView<EquixSolution> solutions, ArrayView<ushort> globalHeap, ArrayView<uint> solutionCount)
        {
            var grid = Grid.GlobalIndex;
            var group = Group.Dimension;

            int blockIndex = (grid.X * group.Y + grid.Y);
            int block = blockIndex / BlockSize;

            values = values.SubView(block * TotalValues, TotalValues);

            var idx = Group.IdxX;

            if (Group.IsFirstThread)
            {
                solutionCount[block] = 0;
            }

            //Group.Barrier();

            var heap = globalHeap.Cast<byte>().SubView(HeapSize * block, HeapSize);


            //1 MB of data
            var tempStage = heap.SubView(TotalValues * 0, TotalValues * 2 * sizeof(ulong)).Cast<ulong>();

            //512KB
            var stageAValues = heap.SubView(TotalValues * 2 * sizeof(ulong), TotalValues * sizeof(ulong)).Cast<ulong>();

            //512KB
            var stageBValues = heap.SubView(TotalValues * 3 * sizeof(ulong), TotalValues * sizeof(ulong)).Cast<ulong>();

            //512KB
            var stage1Indices = heap.SubView(TotalValues * 4 * sizeof(ulong), TotalValues * sizeof(uint)).Cast<uint>();

            //512KB
            var stage2Indices = heap.SubView(TotalValues * 4 * sizeof(ulong) + TotalValues * sizeof(uint), TotalValues * sizeof(uint)).Cast<uint>();

            //256KB
            var tempIndices = heap.SubView(TotalValues * 4 * sizeof(ulong) + TotalValues * sizeof(uint) * 2, TotalValues * sizeof(ushort) * 2).Cast<ushort>();

            #region Shared Memory

            //1KB
            var sharedCounts = SharedMemory.Allocate<int>(numBuckets);  // Count for each time

            //1KB
            var fullCount = SharedMemory.Allocate<int>(numBuckets); //Full counts

            //32KB
            var sharedValues = SharedMemory.Allocate<ulong>(totalSharedBucketItems);  // Shared memory for each block
            var sharedIndices = SharedMemory.Allocate<ushort>(totalSharedBucketItems);  // Shared memory for each block

            #endregion


            int totalNum = SortPass1(idx, values, sharedCounts, fullCount, sharedValues, sharedIndices, tempStage, tempIndices, stageBValues, stage1Indices, TotalValues);
            int totalNum1 = SortPass1(idx, stageBValues, sharedCounts, fullCount, sharedValues, sharedIndices, tempStage, tempIndices, stageAValues, stage2Indices, totalNum);
            SortPass3(idx, stageAValues, sharedCounts, fullCount, sharedValues, sharedIndices, tempStage, tempIndices, stageBValues, stage1Indices, stage2Indices, totalNum1, solutions.Cast<EquixSolution>().SubView(block * 8, 8), solutionCount.SubView(block, 1));

            //if(Group.IsFirstThread && block == 1)
            //{
            //    for(int i =0; i < 16; i++)
            //    {
            //        var v = solutions.Cast<EquixSolution>()[i];

            //        Interop.WriteLine("[{1}] {0}", (uint)v.V0, i);
            //    }
            //}
            //if (idx == 0)
            //{
            //    Interop.WriteLine("{0} {1} {2}", totalNum, totalNum1, totalNum2);
            //}
            //for (int i = 0; i < 8; i++)
            //{
            //    solutions[i] = (ushort)tempCounts[i];
            //}

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SortPass1(int idx, ArrayView<ulong> values, ArrayView<int> sharedCounts, ArrayView<int> fullCount,
                    ArrayView<ulong> sharedValues, ArrayView<ushort> sharedIndices, ArrayView<ulong> tempStage, ArrayView<ushort> tempIndices, ArrayView<ulong> stageBValues, ArrayView<uint> stage1Indices,
                    int totalItems)
        {
            if (idx < numBuckets)
            {
                fullCount[idx] = 0;
            }

            for (int x = 0; x < passes; x++)
            {
                //Clear counts
                if (idx < numBuckets)
                {
                    sharedCounts[idx] = 0;
                }

                Group.Barrier();

                //2048 tiles
                int tileItems = totalItems / passes;

                //Scatter to buckets
                for (int i = idx; i < tileItems; i += BlockSize)
                {
                    var index = x * tileItems + i;
                    var value = values[index] & mask60bit;
                    var bucket = (int)value & 0xFF;

                    var loc = Atomic.Add(ref sharedCounts[bucket], 1);
                    loc = Math.Min(sharedBucketItems - 1, loc);

                    //TileLocation = 11 bits
                    //Index = TileLocation * pass
                    ulong v = value >> 8;
                    ulong tileLocation = (ulong)i << 52;

                    sharedValues[loc + bucket * sharedBucketItems] = v | tileLocation;
                }

                Group.Barrier();

                //Verification of indexes
                //if (idx < 256)
                //{
                //    var totalBucketItems = sharedCounts[idx];

                //    for (int i = 0; i < totalBucketItems; i++)
                //    {
                //        var v = sharedValues[i + idx * sharedBucketItems];

                //        var tLocation = (int)((v) >> 52);
                //        var index = tLocation + (x * tileItems);

                //        var vv = v & ((1ul << 52) - 1);

                //        var globalValue = (values[(int)index] & mask60bit) >> 8;

                //        if (globalValue != vv)
                //        {
                //            //PrintHex(vv);
                //            Interop.WriteLine("{0} != {1} |||| {2}", vv, globalValue, index);
                //        }
                //    }

                //    //Interop.WriteLine("==== END ===");
                //}


                //Group.Barrier();

                //Transfer over chunks
                for (int i = idx; i < totalSharedBucketItems; i += BlockSize)
                {
                    int bucket = i / sharedBucketItems;
                    int itemIndex = i & (sharedBucketItems - 1);

                    int maxItems = Math.Min(sharedBucketItems, sharedCounts[bucket]);
                    ulong value = sharedValues[i];

                    //First thread in bucket
                    if (itemIndex == 0)
                    {
                        Atomic.Add(ref fullCount[bucket], maxItems);
                    }

                    //Buckets are shared between 16 threads in a single warp, so this will work
                    Warp.Barrier();

                    int startIndex = fullCount[bucket] - maxItems;

                    //Only transfer needed
                    if (itemIndex < maxItems)
                    {
                        int index = startIndex + itemIndex;
                        index = Math.Min(maxBucketItems - 1, index);

                        var shortenedValue = value & ((1ul << 52) - 1); //Value without the 8 bytes for bucket
                        var actualValue = (shortenedValue << 8) | (uint)bucket; //Add bucket back in to make things simple
                        var tLocation = (int)(value >> 52);
                        var globalIndex = tLocation + x * tileItems;

                        tempStage[index + bucket * maxBucketItems] = actualValue;
                        tempIndices[index + bucket * maxBucketItems] = (ushort)globalIndex;
                    }
                }
            }

            Group.Barrier();

            const int smallBucketItems = totalSharedBucketItems / 2;
            const int smallBuckets = numBuckets / 2;

            //Will be 128 buckets (7 bits) with 16 values each
            var bucketASValues = sharedValues.SubView(0, smallBucketItems);
            var bucketASIndices = sharedIndices.SubView(0, smallBucketItems);
            var bucketACounts = sharedCounts.SubView(0, smallBuckets);

            var bucketBSValues = sharedValues.SubView(smallBucketItems, smallBucketItems);
            var bucketBSIndices = sharedIndices.SubView(smallBucketItems, smallBucketItems);
            var bucketBCounts = sharedCounts.SubView(smallBuckets, smallBuckets);
            var globalMemoryIndex = SharedMemory.Allocate<int>(1);

            if (idx == 0)
            {
                globalMemoryIndex[0] = 0;
            }

            for (int i = 0; i < numBuckets / 2; i++)
            {
                int bucketAIndex = i;
                int bucketBIndex = -i & 0xFF;

                var totalBucketAItems = Math.Min(maxBucketItems, fullCount[bucketAIndex]);
                var totalBucketBItems = Math.Min(maxBucketItems, fullCount[bucketBIndex]);

                //Clear counts
                if (idx < numBuckets)
                {
                    sharedCounts[idx] = 0;
                }

                Group.Barrier();

                var aValues = tempStage.SubView(bucketAIndex * maxBucketItems, maxBucketItems);
                var bValues = tempStage.SubView(bucketBIndex * maxBucketItems, maxBucketItems);
                var aIndices = tempIndices.SubView(bucketAIndex * maxBucketItems, maxBucketItems);
                var bIndices = tempIndices.SubView(bucketBIndex * maxBucketItems, maxBucketItems);

                RadixSort512_15(idx, totalBucketAItems, bucketACounts, aValues, aIndices, bucketASValues, bucketASIndices);

                Warp.Barrier();

                RadixSort512_15(idx, totalBucketBItems, bucketBCounts, bValues, bIndices, bucketBSValues, bucketBSIndices);

                Group.Barrier();


                //Buckets have no issues


                //8 loops
                //16 items per bucket
                for (int z = idx; z < smallBucketItems; z += BlockSize)
                {
                    int innerBucketA = z / sharedBucketItems;
                    int innerBucketAIndex = z & (sharedBucketItems - 1);

                    int inverseBucketB = -innerBucketA & 0x7F;

                    inverseBucketB = i != 0 ? (inverseBucketB - 1) & 0x7F : inverseBucketB;

                    var bucketACount = bucketACounts[innerBucketA];
                    var bucketBCount = bucketBCounts[inverseBucketB];

                    if (bucketACount > innerBucketAIndex)
                    {
                        var bucketAValue = bucketASValues[z];
                        var bucketAIndice = bucketASIndices[z];

                        var initialLoc = Atomic.Add(ref globalMemoryIndex[0], bucketBCount);

                        initialLoc = Math.Min(initialLoc, TotalValues);

                        if (initialLoc + bucketBCount >= TotalValues)
                        {
                            bucketBCount = TotalValues - initialLoc;
                        }

                        for (int x = 0; x < bucketBCount; x++)
                        {
                            var bucketBValue = bucketBSValues[inverseBucketB * sharedBucketItems + x];
                            var bucketBIndice = bucketBSIndices[inverseBucketB * sharedBucketItems + x];
                            var combined = bucketAValue + bucketBValue;

                            //if (initialLoc + x == 0)
                            //{
                            //    var t1 = values[(int)bucketAIndice] & mask60bit;
                            //    var t2 = values[(int)bucketBIndice] & mask60bit;
                            //    var tt = (uint)(bucketAIndice << 16) | bucketBIndice;

                            //    var i1 = tt & 0xFFFF;
                            //    var i2 = tt >> 16;
                            //    Interop.WriteLine("[{2}] Potential indices: {0} {1}. Combined Value: {3}. Shifted Value: {4}. Global Combined: {5}. Indices: {6} {7}", (uint)bucketAIndice, (uint)bucketBIndice, initialLoc + x, combined, combined >> 15, (t1 + t2), i1, i2);


                            //}

                            stageBValues[initialLoc + x] = combined >> 15;
                            stage1Indices[initialLoc + x] = (uint)(bucketAIndice << 16) | bucketBIndice;
                        }
                    }
                }

                Group.Barrier();
            }

            Group.Barrier();

            int totalValues = Math.Min(TotalValues, globalMemoryIndex[0]);

            //for (int i = idx; i < totalValues; i += BlockSize)
            //{
            //    var v = stageBValues[i];
            //    var index = stage1Indices[i];

            //    var valueA = values[(int)(index & 0xFFFF)] & mask60bit;
            //    var valueB = values[(int)(index >> 16)] & mask60bit;

            //    var combined = valueA + valueB;

            //    if (v != (combined >> 15))
            //    {
            //        Interop.WriteLine("[{2}] {0} != {1}. Indices: {3} {4}", v, combined, i, index & 0xFFFF, index >> 16);
            //    }

            //    if ((combined & 0x7FFF) != 0 || v == 0)
            //    {
            //        Interop.WriteLine("[{0}] Bad", i);
            //    }
            //}

            //if (idx == 0)
            //{
            //    if (totalValues <= 60000)
            //    {
            //        Interop.WriteLine("{0}", totalValues);
            //    }
            //}

            return totalValues;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SortPass3(int idx, ArrayView<ulong> values, ArrayView<int> sharedCounts, ArrayView<int> fullCount,
                                        ArrayView<ulong> sharedValues, ArrayView<ushort> sharedIndices, ArrayView<ulong> tempStage,
                                        ArrayView<ushort> tempIndices, ArrayView<ulong> stageBValues, ArrayView<uint> stage1Indices,
                                        ArrayView<uint> stage2Indices, int totalItems, ArrayView<EquixSolution> solutions, ArrayView<uint> solutionCount)
        {
            if (idx < numBuckets)
            {
                fullCount[idx] = 0;
            }

            for (int x = 0; x < passes; x++)
            {
                //Clear counts
                if (idx < numBuckets)
                {
                    sharedCounts[idx] = 0;
                }

                Group.Barrier();

                //2048 tiles
                int tileItems = totalItems / passes;

                //Scatter to buckets
                for (int i = idx; i < tileItems; i += BlockSize)
                {
                    var index = x * tileItems + i;
                    var value = values[index] & mask60bit;
                    var bucket = (int)value & 0xFF;

                    var loc = Atomic.Add(ref sharedCounts[bucket], 1);
                    loc = Math.Min(sharedBucketItems - 1, loc);

                    //TileLocation = 11 bits
                    //Index = TileLocation * pass
                    ulong v = value >> 8;
                    ulong tileLocation = (ulong)i << 52;

                    sharedValues[loc + bucket * sharedBucketItems] = v | tileLocation;
                }

                Group.Barrier();

                //Verification of indexes
                //if (idx == 0)
                //{
                //    var totalBucketItems = sharedCounts[idx];

                //    for (int i = 0; i < totalBucketItems; i++)
                //    {
                //        var v = sharedValues[i + idx * sharedBucketItems];

                //        var tLocation = (int)((v) >> 52);
                //        var index = tLocation + (x * tileItems);

                //        var vv = v & ((1ul << 52) - 1);

                //        var globalValue = (values[(int)index] & mask60bit) >> 8;

                //        if (globalValue != vv)
                //        {
                //            //PrintHex(vv);
                //            Interop.WriteLine("{0} != {1} |||| {2}", vv, globalValue, index);
                //        }
                //    }

                //    //Interop.WriteLine("==== END ===");
                //}


                //Group.Barrier();

                //Transfer over chunks
                for (int i = idx; i < totalSharedBucketItems; i += BlockSize)
                {
                    int bucket = i / sharedBucketItems;
                    int itemIndex = i & (sharedBucketItems - 1);

                    int maxItems = Math.Min(sharedBucketItems, sharedCounts[bucket]);
                    ulong value = sharedValues[i];

                    //First thread in bucket
                    if (itemIndex == 0)
                    {
                        Atomic.Add(ref fullCount[bucket], maxItems);
                    }

                    //Buckets are shared between 16 threads in a single warp, so this will work
                    Warp.Barrier();

                    int startIndex = fullCount[bucket] - maxItems;

                    //Only transfer needed
                    if (itemIndex < maxItems)
                    {
                        int index = startIndex + itemIndex;
                        index = Math.Min(maxBucketItems - 1, index);

                        var shortenedValue = value & ((1ul << 52) - 1); //Value without the 8 bytes for bucket
                        var actualValue = (shortenedValue << 8) | (uint)bucket; //Add bucket back in to make things simple
                        var tLocation = (int)(value >> 52);
                        var globalIndex = tLocation + x * tileItems;

                        tempStage[index + bucket * maxBucketItems] = actualValue;
                        tempIndices[index + bucket * maxBucketItems] = (ushort)globalIndex;
                    }
                }
            }

            Group.Barrier();

            const int smallBucketItems = totalSharedBucketItems / 2;
            const int smallBuckets = numBuckets / 2;

            //Will be 128 buckets (7 bits) with 16 values each
            var bucketASValues = sharedValues.SubView(0, smallBucketItems);
            var bucketASIndices = sharedIndices.SubView(0, smallBucketItems);
            var bucketACounts = sharedCounts.SubView(0, smallBuckets);

            var bucketBSValues = sharedValues.SubView(smallBucketItems, smallBucketItems);
            var bucketBSIndices = sharedIndices.SubView(smallBucketItems, smallBucketItems);
            var bucketBCounts = sharedCounts.SubView(smallBuckets, smallBuckets);
            var globalMemoryIndex = SharedMemory.Allocate<int>(1);

            if (idx == 0)
            {
                globalMemoryIndex[0] = 0;
            }

            for (int i = 0; i < numBuckets / 2; i++)
            {
                int bucketAIndex = i;
                int bucketBIndex = -i & 0xFF;

                var totalBucketAItems = Math.Min(maxBucketItems, fullCount[bucketAIndex]);
                var totalBucketBItems = Math.Min(maxBucketItems, fullCount[bucketBIndex]);

                //Clear counts
                if (idx < numBuckets)
                {
                    sharedCounts[idx] = 0;
                }

                Group.Barrier();

                var aValues = tempStage.SubView(bucketAIndex * maxBucketItems, maxBucketItems);
                var bValues = tempStage.SubView(bucketBIndex * maxBucketItems, maxBucketItems);
                var aIndices = tempIndices.SubView(bucketAIndex * maxBucketItems, maxBucketItems);
                var bIndices = tempIndices.SubView(bucketBIndex * maxBucketItems, maxBucketItems);

                RadixSort512_15(idx, totalBucketAItems, bucketACounts, aValues, aIndices, bucketASValues, bucketASIndices);

                Warp.Barrier();

                RadixSort512_15(idx, totalBucketBItems, bucketBCounts, bValues, bIndices, bucketBSValues, bucketBSIndices);

                Group.Barrier();

                //Buckets have no issues


                //8 loops
                //16 items per bucket
                for (int z = idx; z < smallBucketItems; z += BlockSize)
                {
                    int innerBucketA = z / sharedBucketItems;
                    int innerBucketAIndex = z & (sharedBucketItems - 1);

                    int inverseBucketB = -innerBucketA & 0x7F;

                    inverseBucketB = i != 0 ? (inverseBucketB - 1) & 0x7F : inverseBucketB;

                    var bucketACount = bucketACounts[innerBucketA];
                    var bucketBCount = bucketBCounts[inverseBucketB];

                    if (bucketACount > innerBucketAIndex)
                    {
                        var bucketAValue = bucketASValues[z];
                        var bucketAIndice = bucketASIndices[z];

                        for (int x = 0; x < bucketBCount; x++)
                        {
                            var bucketBValue = bucketBSValues[inverseBucketB * sharedBucketItems + x];
                            var bucketBIndice = bucketBSIndices[inverseBucketB * sharedBucketItems + x];
                            var combined = bucketAValue + bucketBValue;

                            if ((combined & ((1ul << 30) - 1)) == 0)
                            {
                                //Grab indices
                                var stage2A = stage2Indices[(int)bucketAIndice];
                                var stage2B = stage2Indices[(int)bucketBIndice];

                                var stage2A_indexA = stage2A & 0xFFFF;
                                var stage2A_indexB = stage2A >> 16;
                                var stage2B_indexA = stage2B & 0xFFFF;
                                var stage2B_indexB = stage2B >> 16;

                                var stage1AA = stage1Indices[(int)stage2A_indexA];
                                var stage1AB = stage1Indices[(int)stage2A_indexB];
                                var stage1BA = stage1Indices[(int)stage2B_indexA];
                                var stage1BB = stage1Indices[(int)stage2B_indexB];


                                var index0 = stage1AA & 0xFFFF;
                                var index1 = stage1AA >> 16;


                                var index2 = stage1AB & 0xFFFF;
                                var index3 = stage1AB >> 16;


                                var index4 = stage1BA & 0xFFFF;
                                var index5 = stage1BA >> 16;


                                var index6 = stage1BB & 0xFFFF;
                                var index7 = stage1BB >> 16;

                                var solutionIndex = Atomic.Add(ref solutionCount[0], 1);

                                //Max of 8 solutions
                                solutionIndex = Math.Min(7, solutionIndex);
                                solutions[(int)solutionIndex] = new EquixSolution
                                {
                                    V0 = (ushort)index0,
                                    V1 = (ushort)index1,
                                    V2 = (ushort)index2,
                                    V3 = (ushort)index3,
                                    V4 = (ushort)index4,
                                    V5 = (ushort)index5,
                                    V6 = (ushort)index6,
                                    V7 = (ushort)index7
                                };

                                //Interop.WriteLine("{0} {1} {2} {3} {4} {5} {6} {7}", index0, index1, index2, index3, index4, index5, index6, index7);
                            }
                        }
                    }
                }

                Group.Barrier();
            }

            Group.Barrier();

            int totalValues = globalMemoryIndex[0];

            //for (int i = idx; i < totalValues; i += BlockSize)
            //{
            //    var v = stageBValues[i];
            //    var index = stage1Indices[i];

            //    var valueA = values[(int)index & 0xFFFF] & mask60bit;
            //    var valueB = values[(int)(index >> 16)] & mask60bit;

            //    var combined = valueA + valueB;

            //    if (v != combined)
            //    {
            //        Interop.WriteLine("{0} != {1}", v, combined);
            //    }
            //    if ((stageBValues[i] & 0x7FFF) != 0 || v == 0)
            //    {
            //        Interop.WriteLine("[{0}] Bad", i);
            //    }
            //}

            //if (idx == 0)
            //{
            //    if (totalValues <= 60000)
            //    {
            //        Interop.WriteLine("{0}", totalValues);
            //    }
            //}

            return totalValues;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RadixSort512_15(int idx, int max, ArrayView<int> counts, ArrayView<ulong> values, ArrayView<ushort> indices, ArrayView<ulong> output, ArrayView<ushort> indiceOutput)
        {
            //Grab bucket
            //for(int i = idx; i < max; i += BlockSize)
            {
                ulong value = values[idx];
                ushort indice = indices[idx];

                int bucket = (int)((value >> 8) & 0x7F);

                if (idx < max)
                {
                    //Scatter
                    int loc = Atomic.Add(ref counts[bucket], 1);

                    loc = Math.Min(sharedBucketItems - 1, loc);

                    //It's possible to store the indice within the value
                    output[bucket * sharedBucketItems + loc] = value;
                    indiceOutput[bucket * sharedBucketItems + loc] = indice;
                }
            }
        }

    }
}