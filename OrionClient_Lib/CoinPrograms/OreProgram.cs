﻿using NLog;
using OrionClientLib.CoinPrograms.Ore;
using Solnet.Programs;
using Solnet.Programs.Utilities;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using System.Text;

namespace OrionClientLib.CoinPrograms
{
    public class OreProgram
    {
        private static readonly Logger _logger = LogManager.GetLogger("Main");
        private static readonly PublicKey SlotHashesKey = new("SysvarS1otHashes111111111111111111111111111");
        private static readonly PublicKey Instructions = new("Sysvar1nstructions1111111111111111111111111");


        public enum Errors
        {
            Unknown = -1,
            NeedsReset = 0,
            HashInvalid = 1,
            HashTooEasy = 2,
            ClaimTooLarge = 3,
            ClockInvalid = 4,
            Spam = 5,
            MaxSupply = 6,
            AuthFailed = 7,
            MiningDisabled = 8,


            BlockhashNotFound = 1000
        };

        private enum Instruction { Claim = 0, Close, Mine, Open, Reset, Stake, Update, Upgrade, Initialize = 100 };

        public static readonly PublicKey[] BusIds = new PublicKey[8];

        public static PublicKey ConfigAddress;
        public static PublicKey TreasuryId;
        public static PublicKey TreasuryATAId;
        public static PublicKey TreasuryOreATAId;
        public static PublicKey MintId;
        //public static PublicKey BoostDirectoryId;

        public static PublicKey ProgramId = new PublicKey("oreV2ZymfyeXgNgBdqMkumTqqAprVqgBWQfoYkrtKWQ");
        public static readonly PublicKey NoopId = new PublicKey("noop8ytexvkpCuqbf6FB89BSuNemHtPRqaNC31GWivW");
        public static readonly PublicKey BoostProgramId = new PublicKey("BoostzzkNfCA9D1qNuN5xZxB5ErbK4zQuBeTHGDpXT1");
        public static readonly PublicKey BoostAuthority = new PublicKey("HBUh9g46wk2X89CvaNN15UmsznP59rh6od1h8JwYAopk");
        public static PublicKey BoostConfig;
        public static readonly PublicKey BoostCheckpointId = new PublicKey("6qWtSWTmWRgzmLMMpPAgzckKy73BkzXWqoZun6usqdCM");

        public static readonly List<BoostInformation> Boosts = new List<BoostInformation>()
        {
            new BoostInformation("oreoU2P8bN6jkk3jbaiVxYnG1dCXcYxwhwyK9jSybcp", 11, "Ore", BoostInformation.PoolType.Ore, null, "ore"),
            new BoostInformation("8H8rPiWW4iTFCfEkSnf7jpqeNpFfvdH9gLouAL3Fe2Zx", 6, "Ore-Sol (Kamino)", BoostInformation.PoolType.Kamino, "6TFdY15Mxty9sRCtzMXG8eHSbZy4oiAEQUvLQdz9YwEn", "solana"),
            //new BoostInformation("7G3dfZkSk1HpDGnyL37LMBbPEgT4Ca6vZmZPUyi2syWt", 6, "Ore-Hnt (Kamino)", BoostInformation.PoolType.Kamino, "9XsAPjk1yp4U6hKZj9r9szhcxBi3RidGuyxiC2Y8JtAe", "helium"),
            //Index 168 and 200 of LP pool data for vaults
            new BoostInformation("DrSS5RM7zUd9qjUEdDaf31vnDUSbCrMto6mjqTrHFifN", 11, "Ore-Sol (Meteora)", BoostInformation.PoolType.Meteora, "GgaDTFbqdgjoZz3FP7zrtofGwnRS4E6MCzmmD5Ni1Mxj", "solana",
                new BoostInformation.MeteoraExtraData("2k7V1NtM1krwh1sdt5wWqBRcvNQ5jzxj3J2rV78zdTsL",
                                                      "CFATQFgkKXJyU3MdCNvQqN79qorNSMJFF8jrF66a7r6i",
                                                      "3s6ki6dQSM8FuqWiPsnGkgVsAEo8BTAfUR1Vvt1TPiJN",
                                                      "FERjPVNEa7Udq8CEv68h6tPL46Tq7ieE49HrE2wea3XT",
                                                      "6Av9sdKvnjwoDHVnhEiz6JEq8e6SGzmhCsCncT2WJ7nN",
                                                      "FZN7QZ8ZUUAxMPfxYEYkH3cXUASzH8EqA6B4tyCL8f1j", 9)),
                new BoostInformation("9BAWwtAZiF4XJC6vArPM8JhtgKXfeoeo9FJHeR3PEGac", 11, "Ore-Usdc (Meteora)", BoostInformation.PoolType.Meteora, "7XNR3Ysqg2MbfQX8iMWD4iEF96h2GMsWNT8eZYsLTmua", "usd",
                new BoostInformation.MeteoraExtraData("A9Nt1w73vS1W7kphM3ykoqYqunjq86a18LWcegXWDewk",
                                                      "3YiQeRH8i4fopVYHXwrpZ55chmr77dYhCvsmdAuPyEQg",
                                                      "3s6ki6dQSM8FuqWiPsnGkgVsAEo8BTAfUR1Vvt1TPiJN",
                                                      "3ESUFCnRNgZ7Mn2mPPUMmXYaKU8jpnV9VtA17M7t2mHQ",
                                                      "6Av9sdKvnjwoDHVnhEiz6JEq8e6SGzmhCsCncT2WJ7nN",
                                                      "3RpEekjLE5cdcG15YcXJUpxSepemvq2FpmMcgo342BwC", 6)),
            //new BoostInformation(new PublicKey("meUwDp23AaxhiNKaQCyJ2EAF2T4oe1gSkEkGXSRVdZb"), 11, "Ore-ISC (Meteroa)", BoostInformation.PoolType.Meteroa,new PublicKey("2vo5uC7jbmb1zNqYpKZfVyewiQmRmbJktma4QHuGNgS5"), "international-stable-currency"),
        };


        public static readonly double OreDecimals = Math.Pow(10, 11);
        private static readonly byte[] MintNoise = new byte[] { 89, 157, 88, 232, 243, 249, 197, 132, 199, 49, 19, 234, 91, 94, 150, 41 };

        static OreProgram()
        {
            Initialize(ProgramId);
        }

        public static void Initialize(PublicKey programId)
        {
            ProgramId = programId;

            //Generate busses
            for (int i = 0; i < BusIds.Length; i++)
            {
                PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("bus"), new byte[] { (byte)i } }, ProgramId, out var publicKey, out byte nonce);

                BusIds[i] = publicKey;
            }

            PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("treasury") }, ProgramId, out var b, out var n);
            TreasuryId = b;

            PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("mint"), MintNoise }, ProgramId, out b, out n);
            MintId = b;


            PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("config") }, ProgramId, out b, out n);
            ConfigAddress = b;

            PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("config") }, BoostProgramId, out b, out n);
            BoostConfig = b;


            //PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("directory") }, BoostProgramId, out b, out n);
            //BoostDirectoryId = b;

            TreasuryATAId = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(TreasuryId, MintId);
        }

        public static (PublicKey key, uint nonce) GetProofKey(PublicKey signer, PublicKey programId)
        {
            if (PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("proof"), signer.KeyBytes }, programId, out PublicKey address, out byte nonce))
            {
                return (address, nonce);
            }

            return default;
        }

        public static TransactionInstruction Register(PublicKey programId, PublicKey signer, PublicKey minerAuthority, PublicKey fundingWallet, PublicKey systemProgram, PublicKey slotHashes)
        {
            var proof = GetProofKey(signer, programId);

            List<AccountMeta> keys = new()
            {
                AccountMeta.Writable(signer, true),
                AccountMeta.ReadOnly(minerAuthority, false),
                AccountMeta.Writable(fundingWallet, true),
                AccountMeta.Writable(proof.key, false),
                AccountMeta.ReadOnly(systemProgram, false),
                AccountMeta.ReadOnly(slotHashes, false),
            };

            byte[] data = new byte[2];
            data[0] = (byte)Instruction.Open;
            data.WriteU8((byte)proof.nonce, 1);

            return new TransactionInstruction
            {
                ProgramId = programId,
                Keys = keys,
                Data = data
            };
        }

        public static TransactionInstruction Close(PublicKey programId, PublicKey signer, PublicKey systemProgram)
        {
            var proof = GetProofKey(signer, programId);

            List<AccountMeta> keys = new()
            {
                AccountMeta.Writable(signer, true),
                AccountMeta.Writable(proof.key, false),
                AccountMeta.ReadOnly(systemProgram, false),
            };

            byte[] data = new byte[1];
            data[0] = (byte)Instruction.Close;

            return new TransactionInstruction
            {
                ProgramId = programId,
                Keys = keys,
                Data = data
            };
        }

        public static TransactionInstruction Mine(PublicKey programId, PublicKey signer, PublicKey bus, PublicKey proof, byte[] solution, ulong nonce)
        {
            List<AccountMeta> keys = new()
            {
                AccountMeta.Writable(signer, true),
                AccountMeta.Writable(bus, false),
                AccountMeta.ReadOnly(ConfigAddress, false),
                AccountMeta.Writable(proof, false),
                AccountMeta.ReadOnly(Instructions, false),
                AccountMeta.ReadOnly(SlotHashesKey, false),
            };

            byte[] data = new byte[25];
            data[0] = (byte)Instruction.Mine;
            data.WriteSpan(solution, 1); //16 bytes
            data.WriteU64(nonce, 17); //8 bytes

            return new TransactionInstruction
            {
                ProgramId = programId,
                Keys = keys,
                Data = data
            };
        }

        public static TransactionInstruction Claim(PublicKey programId, PublicKey signer, PublicKey beneficiary, PublicKey proof, PublicKey teasury, PublicKey teasuryATA, PublicKey splTokenProgram, ulong claimAmount)
        {
            List<AccountMeta> keys = new()
            {
                AccountMeta.Writable(signer, true),
                AccountMeta.Writable(beneficiary, false),
                AccountMeta.Writable(proof, false),
                AccountMeta.ReadOnly(teasury, false),
                AccountMeta.Writable(teasuryATA, false),
                AccountMeta.ReadOnly(splTokenProgram, false),
            };

            byte[] data = new byte[9];
            data[0] = (byte)Instruction.Claim;
            data.WriteU64(claimAmount, 1);

            return new TransactionInstruction
            {
                ProgramId = programId,
                Keys = keys,
                Data = data
            };
        }

        public static TransactionInstruction Auth(PublicKey proof)
        {
            List<AccountMeta> keys = new();

            byte[] data = proof.KeyBytes;

            return new TransactionInstruction
            {
                ProgramId = NoopId,
                Keys = keys,
                Data = data
            };
        }

        public static PublicKey DeriveBoost(PublicKey mint)
        {
            if (PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("boost"), mint.KeyBytes }, BoostProgramId, out PublicKey address, out byte nonce))
            {
                return address;
            }

            return default;
        }

        public static PublicKey DeriveCheckpoint(PublicKey boost)
        {
            if (PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("checkpoint"), boost.KeyBytes }, BoostProgramId, out PublicKey address, out byte nonce))
            {
                return address;
            }

            return default;
        }

        public static PublicKey DeriveStakeAccount(PublicKey boost, PublicKey authority)
        {
            if (PublicKey.TryFindProgramAddress(new List<byte[]> { Encoding.UTF8.GetBytes("stake"), authority.KeyBytes, boost.KeyBytes }, BoostProgramId, out PublicKey address, out byte nonce))
            {
                return address;
            }

            return default;
        }
    }
}
