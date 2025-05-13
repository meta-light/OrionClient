
using DrillX;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace Equix
{
    public unsafe class Haraka
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static HarakaSiphash BuildKey(Span<byte> fullChallenge, Span<HarakaSiphash> output)
        {
            Blake2Fast.Blake2s.ComputeAndWriteHash(32, fullChallenge, MemoryMarshal.Cast<HarakaSiphash, byte>(output));

            return output[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong HashInput(ulong input, ulong* roundConstants, HarakaSiphash sipKey)
        {
            //Siphash24ctr

            ulong v0 = sipKey.V[0];
            ulong v1 = sipKey.V[1] ^ 0xee;
            ulong v2 = sipKey.V[2];
            ulong v3 = sipKey.V[3] ^ input;

            SipRoundLow();
            SipRoundLow();

            v0 ^= input;
            v2 ^= 0xee;

            SipRoundLow();
            SipRoundLow();
            SipRoundLow();
            SipRoundLow();


            ulong v4 = v0;
            ulong v5 = v1 ^ 0xdd;
            ulong v6 = v2;
            ulong v7 = v3;

            SipRoundHigh();
            SipRoundHigh();
            SipRoundHigh();
            SipRoundHigh();

            return Haraka512(v0, v1, v2, v3, v4, v5, v6, v7, roundConstants);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SipRoundLow()
            {
                v0 = v0 + v1;
                v2 = v2 + v3;
                v1 = v1.Rol(13);
                v3 = v3.Rol(16);
                v1 ^= v0;
                v3 ^= v2;
                v0 = v0.Rol(32);

                v2 = v2 + v1;
                v0 = v0 + v3;
                v1 = v1.Rol(17);
                v3 = v3.Rol(21);
                v1 ^= v2;
                v3 ^= v0;
                v2 = v2.Rol(32);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SipRoundHigh()
            {
                v4 = v4 + v5;
                v6 = v6 + v7;
                v5 = v5.Rol(13);
                v7 = v7.Rol(16);
                v5 ^= v4;
                v7 ^= v6;
                v4 = v4.Rol(32);

                v6 = v6 + v5;
                v4 = v4 + v7;
                v5 = v5.Rol(17);
                v7 = v7.Rol(21);
                v5 ^= v6;
                v7 ^= v4;
                v6 = v6.Rol(32);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Haraka512(ulong v0, ulong v1, ulong v2, ulong v3, ulong v4, ulong v5, ulong v6, ulong v7, ulong* roundConstants)
        {
            Vector128<byte> s0 = Vector128.Create(v0, v1).AsByte();
            Vector128<byte> s1 = Vector128.Create(v2, v3).AsByte();
            Vector128<byte> s2 = Vector128.Create(v4, v5).AsByte();
            Vector128<byte> s3 = Vector128.Create(v6, v7).AsByte();

            {
                AESEncrypt(roundConstants);
                AESEncrypt(roundConstants + 16);
                AESEncrypt(roundConstants + 32);
                AESEncrypt(roundConstants + 48);
                AESEncrypt(roundConstants + 64);
                AESEncrypt(roundConstants + 80);

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                void AESEncrypt(ulong* roundConstants)
                {
                    s0 = Aes.Encrypt(s0, Avx.LoadVector128(roundConstants).AsByte());
                    s1 = Aes.Encrypt(s1, Avx.LoadVector128(roundConstants + 2).AsByte());
                    s2 = Aes.Encrypt(s2, Avx.LoadVector128(roundConstants + 4).AsByte());
                    s3 = Aes.Encrypt(s3, Avx.LoadVector128(roundConstants + 6).AsByte());

                    s0 = Aes.Encrypt(s0, Avx.LoadVector128(roundConstants + 8).AsByte());
                    s1 = Aes.Encrypt(s1, Avx.LoadVector128(roundConstants + 10).AsByte());
                    s2 = Aes.Encrypt(s2, Avx.LoadVector128(roundConstants + 12).AsByte());
                    s3 = Aes.Encrypt(s3, Avx.LoadVector128(roundConstants + 14).AsByte());

                    var tmp = Avx.UnpackLow(s0.AsInt32(), s1.AsInt32()).AsByte();
                    s0 = Avx.UnpackHigh(s0.AsInt32(), s1.AsInt32()).AsByte();

                    s1 = Avx.UnpackLow(s2.AsInt32(), s3.AsInt32()).AsByte();
                    s2 = Avx.UnpackHigh(s2.AsInt32(), s3.AsInt32()).AsByte();

                    s3 = Avx.UnpackLow(s0.AsInt32(), s2.AsInt32()).AsByte();
                    s0 = Avx.UnpackHigh(s0.AsInt32(), s2.AsInt32()).AsByte();

                    s2 = Avx.UnpackHigh(s1.AsInt32(), tmp.AsInt32()).AsByte();
                    s1 = Avx.UnpackLow(s1.AsInt32(), tmp.AsInt32()).AsByte();
                }

                Vector128<byte> t0 = Vector128.Create(v0, v1).AsByte();
                Vector128<byte> t1 = Vector128.Create(v2, v3).AsByte();
                Vector128<byte> t2 = Vector128.Create(v4, v5).AsByte();
                Vector128<byte> t3 = Vector128.Create(v6, v7).AsByte();

                t0 = Avx.Xor(s0, t0);
                t1 = Avx.Xor(s1, t1);
                t2 = Avx.Xor(s2, t2);
                t3 = Avx.Xor(s3, t3);

                var a0 = Avx2.UnpackHigh(t0.AsUInt64(), t1.AsUInt64());
                var a1 = Avx2.UnpackLow(t2.AsUInt64(), t3.AsUInt64());

                var b0 = Avx2.Xor(a0, a1);
                var b0_reversed = Sse2.Shuffle(b0.AsDouble(), b0.AsDouble(), 0b01_01).AsUInt64();

                return Avx2.Xor(b0, b0_reversed)[0];
            }
        }


        public struct HarakaSiphash
        {
            public fixed ulong V[4];

            public static HarakaSiphash Create(ReadOnlySpan<byte> input)
            {
                return MemoryMarshal.Cast<byte, HarakaSiphash>(input)[0];
            }
        }
    }
}
