using OrionClientLib.Hashers.Models;
using OrionClientLib.Pools.Models;
using Solnet.Wallet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrionClientLib.Pools
{
   public class NoPool : IPool
   {
       public string Name { get; } = "Bitz Solo";
       public string DisplayName { get; } = "Bitz Solo";
       public string Description { get; } = "Bitz Solo Mining";
       public string ArgName { get; } = "bitzsolo";
       public bool HideOnPoolList { get; } = false;
       public Coin Coins { get; } = Coin.Bitz;
       public bool RequiresKeypair { get; } = true;

       public Dictionary<string, string> Features => new Dictionary<string, string>
       {
           { "Solo Mining", "Mine directly to your wallet without a pool" },
           { "No Pool Fees", "Keep 100% of your mining rewards" },
           { "Direct Rewards", "Rewards go directly to your wallet" }
       };

       public event EventHandler<NewChallengeInfo> OnChallengeUpdate;
       public event EventHandler<(string[] columns, byte[] data)> OnMinerUpdate;
       public event EventHandler PauseMining;
       public event EventHandler ResumeMining;

       private Wallet _wallet;
       private string _publicKey;
       private System.Timers.Timer _challengeTimer;
       private int _challengeId = 0;
       private byte[] _currentChallenge;
       private ulong _startNonce;
       private ulong _endNonce;
       private double _miningRewards = 0;
       private double _walletBalance = 0;

       public void SetWalletInfo(Wallet wallet, string publicKey)
       {
           _wallet = wallet;
           _publicKey = publicKey;
       }

       public async Task<bool> ConnectAsync(CancellationToken token)
       {
           _challengeTimer = new System.Timers.Timer(30000);
           _challengeTimer.Elapsed += (sender, e) => GenerateNewChallenge();
           _challengeTimer.Start();

           GenerateNewChallenge();
           return true;
       }

       public async Task<bool> DisconnectAsync()
       {
           _challengeTimer?.Stop();
           _challengeTimer?.Dispose();
           return true;
       }

       public async Task<double> GetFeeAsync(CancellationToken token)
       {
           return 0.0;
       }

       public async Task<(bool success, string errorMessage)> SetupAsync(CancellationToken token, bool initialSetup = false)
       {
           if (_wallet == null)
           {
               return (false, "A full keypair is required for solo mining");
           }
           return (true, string.Empty);
       }

       public async Task<(bool success, string errorMessage)> OptionsAsync(CancellationToken token)
       {
           return (true, string.Empty);
       }

       public string[] TableHeaders()
       {
           return new[] { "Time", "Challenge ID", "Nonce Range", "Mining Rewards", "Wallet Balance" };
       }

       public void DifficultyFound(DifficultyInfo info)
       {

       }

       private void GenerateNewChallenge()
       {
           _challengeId++;
           _currentChallenge = new byte[32];
           new Random().NextBytes(_currentChallenge);
           _startNonce = 0;
           _endNonce = ulong.MaxValue;

           OnChallengeUpdate?.Invoke(this, new NewChallengeInfo
           {
               ChallengeId = _challengeId,
               Challenge = _currentChallenge,
               StartNonce = _startNonce,
               EndNonce = _endNonce,
               TotalCPUNonces = _endNonce / 2 
           });

           OnMinerUpdate?.Invoke(this, (
               new[] { 
                   DateTime.Now.ToShortTimeString(),
                   _challengeId.ToString(),
                   $"{_startNonce}-{_endNonce}",
                   $"{_miningRewards:0.00000000000} {Coins}",
                   $"{_walletBalance:0.00000000000} {Coins}"
               },
               null
           ));
       }

       public void UpdateMiningRewards(double rewards)
       {
           _miningRewards = rewards;
           GenerateNewChallenge();
       }

       public void UpdateWalletBalance(double balance)
       {
           _walletBalance = balance;
           GenerateNewChallenge();
       }
   }
}
