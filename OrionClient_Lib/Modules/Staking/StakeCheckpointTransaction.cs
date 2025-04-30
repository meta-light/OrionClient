namespace OrionClientLib.Modules.Staking
{
    public class StakeCheckpointTransaction
    {
        public string Signature { get; set; }
        public bool DataPulled { get; set; }
        public bool CheckpointStarted => RewardAmount > 0;
        public ulong RewardAmount { get; set; }
        public long Timestamp { get; set; }
    }
}
