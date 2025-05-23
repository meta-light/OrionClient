using OrionClientLib.Hashers.Models;
using OrionClientLib.Pools.Models;
using OrionClientLib.CoinPrograms;
using Solnet.Rpc;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using NLog;

namespace OrionClientLib.Pools
{
    public class BitzNoPool : BasePool
    {
        private static readonly Logger _logger = LogManager.GetLogger("Main");

        public override string Name => "Bitz Solo";
        public override string DisplayName => "Bitz Solo";
        public override string Description => "Bitz Solo Mining - Mine directly to the Eclipse blockchain";
        public override string ArgName => "bitzsolo";
        public override bool DisplaySetting => true;
        public override bool HideOnPoolList => false;
        public override Coin Coins => Coin.Bitz;
        public override bool RequiresKeypair => true;

        public override Dictionary<string, string> Features => new Dictionary<string, string>
       {
           { "Solo Mining", "Mine directly to your wallet without a pool" },
           { "No Pool Fees", "Keep 100% of your mining rewards" },
            { "Eclipse Network", "Mines on Eclipse mainnet blockchain" }
        };

        public override event EventHandler<NewChallengeInfo> OnChallengeUpdate;
        public override event EventHandler<(string[] columns, byte[] data)> OnMinerUpdate;
        public override event EventHandler PauseMining;
        public override event EventHandler ResumeMining;

       private Wallet _wallet;
       private string _publicKey;
        private IRpcClient _rpcClient;
        private Settings _settings;
       private System.Timers.Timer _challengeTimer;
       private int _challengeId = 0;
       private byte[] _currentChallenge;
        private DifficultyInfo _bestDifficulty;
       private double _miningRewards = 0;
       private double _walletBalance = 0;
        private PublicKey _proofAccount;
        private bool _proofAccountExists = false;

        public override void SetWalletInfo(Wallet wallet, string publicKey)
       {
           _wallet = wallet;
           _publicKey = publicKey;

            if (_wallet != null)
            {
                // Get proof account for this wallet
                var proofKey = BitzProgram.GetProofKey(_wallet.Account.PublicKey, BitzProgram.ProgramId);
                _proofAccount = proofKey.key;
            }
        }

        public override async Task<bool> ConnectAsync(CancellationToken token)
        {
            try
            {
                // Load settings and initialize RPC client for ECLIPSE BLOCKCHAIN (not Solana)
                if (_settings == null)
                {
                    _settings = await Settings.LoadAsync();
                    BitzProgram.SetBitzRpcSettings(_settings.BitzRPCSetting);
                    _rpcClient = BitzProgram.GetRpcClient(); // Eclipse RPC, not Solana RPC
                    
                    // Log to confirm we're using Eclipse
                    _logger.Log(LogLevel.Info, $"Connected to Eclipse RPC: {_settings.BitzRPCSetting.Url}");
                }

                // Test RPC connectivity first
                _logger.Log(LogLevel.Info, "Testing Eclipse RPC connectivity...");
                try
                {
                    var healthCheck = await _rpcClient.GetHealthAsync();
                    if (healthCheck.WasSuccessful)
                    {
                        _logger.Log(LogLevel.Info, "✅ Eclipse RPC health check passed");
                    }
                    else
                    {
                        _logger.Log(LogLevel.Warn, $"⚠️ Eclipse RPC health check failed: {healthCheck.Reason}");
                    }

                    // Try to get latest blockhash as secondary test
                    var blockTest = await _rpcClient.GetLatestBlockHashAsync();
                    if (blockTest.WasSuccessful)
                    {
                        _logger.Log(LogLevel.Info, $"✅ Eclipse RPC blockhash test passed: {blockTest.Result.Value.Blockhash}");
                    }
                    else
                    {
                        _logger.Log(LogLevel.Error, $"❌ Eclipse RPC blockhash test failed: {blockTest.Reason}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"❌ Eclipse RPC connectivity test failed: {ex.Message}");
                    return false;
                }

                // Verify we're using BITZ program IDs (not ORE)
                _logger.Log(LogLevel.Debug, $"Using Bitz Program ID: {BitzProgram.ProgramId}");
                _logger.Log(LogLevel.Debug, $"Using Bitz Noop ID: {BitzProgram.NoopId}");
                _logger.Log(LogLevel.Debug, $"Using Bitz Mint: {BitzProgram.MintId}");

                // Check if proof account exists
                await CheckProofAccountAsync();

                // Set up challenge timer (like ExamplePool)
                _challengeTimer = new System.Timers.Timer(30000); // 30 seconds like ORE
                _challengeTimer.Elapsed += (sender, e) =>
                {
                    // Update miner table with current stats
                    OnMinerUpdate?.Invoke(this, (
                        new[] { 
                            DateTime.Now.ToShortTimeString(),
                            _challengeId.ToString(),
                            _bestDifficulty?.BestDifficulty.ToString() ?? "0",
                            $"{_miningRewards:0.00000000000} {Coins}",
                            $"{_walletBalance:0.00000000000} {Coins}"
                        },
                        null
                    ));

                    // Reset best difficulty and generate new challenge
                    _bestDifficulty = null;
                    GenerateNewChallenge();
                };
           _challengeTimer.Start();

                // Start wallet balance updates
                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        await UpdateWalletBalanceAsync();
                        await Task.Delay(10000, token); // Update every 10 seconds
                    }
                });

                // Generate initial challenge
           GenerateNewChallenge();

           return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error connecting to Bitz solo pool: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> DisconnectAsync()
       {
           _challengeTimer?.Stop();
           _challengeTimer?.Dispose();
           return true;
       }

        public override async Task<double> GetFeeAsync(CancellationToken token)
       {
            return 0.0; // No fees for solo mining
       }

        public override async Task<(bool success, string errorMessage)> SetupAsync(CancellationToken token, bool initialSetup = false)
       {
            Console.WriteLine("DEBUG: BitzNoPool.SetupAsync called!");
            Console.WriteLine($"DEBUG: initialSetup = {initialSetup}");
            
           if (_wallet == null)
           {
                Console.WriteLine("DEBUG: Wallet is null, returning error");
               return (false, "A full keypair is required for solo mining");
           }

            try
            {
                Console.WriteLine("DEBUG: Starting Bitz setup...");
                Console.WriteLine($"DEBUG: Wallet: {_wallet.Account.PublicKey}");
                Console.WriteLine($"DEBUG: RPC Client: {_rpcClient != null}");
                Console.WriteLine($"DEBUG: Settings: {_settings != null}");

                // Initialize RPC client and settings if not already done
                // (SetupAsync is called before ConnectAsync in the setup flow)
                if (_settings == null || _rpcClient == null)
                {
                    Console.WriteLine("DEBUG: Initializing settings and RPC client...");
               _settings = await Settings.LoadAsync();
               BitzProgram.SetBitzRpcSettings(_settings.BitzRPCSetting);
               _rpcClient = BitzProgram.GetRpcClient();
                    
                    Console.WriteLine($"DEBUG: Settings loaded: {_settings != null}");
                    Console.WriteLine($"DEBUG: RPC Client initialized: {_rpcClient != null}");
                    Console.WriteLine($"DEBUG: Eclipse RPC URL: {_settings.BitzRPCSetting.Url}");
                }

                // Check if proof account already exists first
                Console.WriteLine("DEBUG: Checking if proof account already exists...");
                await CheckProofAccountAsync();
                Console.WriteLine($"DEBUG: Proof account exists check result: {_proofAccountExists}");

                // Get on-chain data for debugging
                await LogOnChainDataAsync();

                // Also check the mystery accounts from the real Bitz transaction
                Console.WriteLine("=== MYSTERY ACCOUNTS FROM REAL BITZ TX ===");
                
                // Account #7 from real transaction: 5wpgyJFziVdB2RHW3qUx7teZxCax5FsniZxELdxiKUFD
                var account7 = new PublicKey("5wpgyJFziVdB2RHW3qUx7teZxCax5FsniZxELdxiKUFD");
                Console.WriteLine($"DEBUG: Fetching Account #7: {account7}");
                var account7Info = await _rpcClient.GetAccountInfoAsync(account7);
                if (account7Info.WasSuccessful && account7Info.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Account #7 - Owner: {account7Info.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Account #7 - Lamports: {account7Info.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Account #7 - Data Length: {account7Info.Result.Value.Data?.Count ?? 0}");
                    Console.WriteLine($"DEBUG: Account #7 - Executable: {account7Info.Result.Value.Executable}");
                }
                else
                {
                    Console.WriteLine($"DEBUG: Account #7 - NOT FOUND or RPC failed: {account7Info.Reason}");
                }

                // Account #8 from real transaction: 3YiLgGTS23imzTfkTZhfTzNDtiz1mrrQoB4f3yyFUByE
                var account8 = new PublicKey("3YiLgGTS23imzTfkTZhfTzNDtiz1mrrQoB4f3yyFUByE");
                Console.WriteLine($"DEBUG: Fetching Account #8: {account8}");
                var account8Info = await _rpcClient.GetAccountInfoAsync(account8);
                if (account8Info.WasSuccessful && account8Info.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Account #8 - Owner: {account8Info.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Account #8 - Lamports: {account8Info.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Account #8 - Data Length: {account8Info.Result.Value.Data?.Count ?? 0}");
                    Console.WriteLine($"DEBUG: Account #8 - Executable: {account8Info.Result.Value.Executable}");
                }
                else
                {
                    Console.WriteLine($"DEBUG: Account #8 - NOT FOUND or RPC failed: {account8Info.Reason}");
                }

                Console.WriteLine("=== END MYSTERY ACCOUNTS ===");

                // Ensure proof account exists
                if (!_proofAccountExists)
                {
                    Console.WriteLine("DEBUG: Proof account doesn't exist, creating...");
                    var created = await CreateProofAccountAsync();
                    Console.WriteLine($"DEBUG: CreateProofAccountAsync returned: {created}");
                    
                    if (!created)
                    {
                        var errorMsg = "Failed to create proof account - check console for detailed error logs";
                        Console.WriteLine($"DEBUG: Setup failed: {errorMsg}");
                        return (false, errorMsg);
                    }
                }
                else
                {
                    Console.WriteLine("DEBUG: Proof account already exists, skipping creation");
                }

                Console.WriteLine("DEBUG: Setup completed successfully");
           return (true, string.Empty);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Setup failed with exception: {ex.Message}";
                Console.WriteLine($"DEBUG: Exception in Setup: {errorMsg}");
                Console.WriteLine($"DEBUG: Stack trace: {ex.StackTrace}");
                return (false, errorMsg);
            }
        }

        public override async Task<(bool success, string errorMessage)> OptionsAsync(CancellationToken token)
        {
            // Could add balance viewing, etc. here later
           return (true, string.Empty);
       }

        public override string[] TableHeaders()
        {
            return new[] { "Time", "Challenge ID", "Best Difficulty", "Mining Rewards", "Wallet Balance" };
        }

        public override void DifficultyFound(DifficultyInfo info)
        {
            // Wrong challenge ID, ignore
            if (info.ChallengeId != _challengeId)
            {
                return;
            }

            // Track best difficulty for this challenge
            if (_bestDifficulty == null || _bestDifficulty.BestDifficulty < info.BestDifficulty)
            {
                _bestDifficulty = info;
            }

            // Submit solution if it meets minimum difficulty (like ORE)
            if (info.BestDifficulty >= 10) // Minimum difficulty threshold
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SubmitSolutionAsync(info);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Warn, $"Error submitting solution: {ex.Message}");
                    }
                });
            }
        }

        private async Task<bool> CheckProofAccountAsync()
        {
            try
            {
                if (_rpcClient == null || _proofAccount == null)
                {
                    Console.WriteLine("DEBUG: CheckProofAccount - RPC client or proof account is null");
                    _logger.Log(LogLevel.Error, "CheckProofAccount: RPC client or proof account is null");
                    return false;
                }

                Console.WriteLine($"DEBUG: CheckProofAccount - Checking account: {_proofAccount}");
                _logger.Log(LogLevel.Debug, $"Checking if proof account exists: {_proofAccount}");
                var accountInfo = await _rpcClient.GetAccountInfoAsync(_proofAccount);
                
                Console.WriteLine($"DEBUG: CheckProofAccount - RPC call successful: {accountInfo.WasSuccessful}");
                Console.WriteLine($"DEBUG: CheckProofAccount - Account result null: {accountInfo.Result?.Value == null}");
                
                if (accountInfo.WasSuccessful)
                {
                    if (accountInfo.Result?.Value != null)
                    {
                        Console.WriteLine($"DEBUG: CheckProofAccount - Account data length: {accountInfo.Result.Value.Data?.Count ?? 0}");
                        Console.WriteLine($"DEBUG: CheckProofAccount - Account owner: {accountInfo.Result.Value.Owner}");
                        Console.WriteLine($"DEBUG: CheckProofAccount - Account executable: {accountInfo.Result.Value.Executable}");
                        Console.WriteLine($"DEBUG: CheckProofAccount - Account lamports: {accountInfo.Result.Value.Lamports}");
                    }
                    else
                    {
                        Console.WriteLine("DEBUG: CheckProofAccount - Account result value is null (account doesn't exist)");
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG: CheckProofAccount - RPC call failed: {accountInfo.Reason}");
                    Console.WriteLine($"DEBUG: CheckProofAccount - HTTP status: {accountInfo.HttpStatusCode}");
                }
                
                _proofAccountExists = accountInfo.WasSuccessful && accountInfo.Result?.Value != null;
                
                if (_proofAccountExists)
                {
                    Console.WriteLine("DEBUG: CheckProofAccount - Account EXISTS, skipping creation");
                    _logger.Log(LogLevel.Info, $"✅ Proof account already exists: {_proofAccount}");
                }
                else
                {
                    Console.WriteLine("DEBUG: CheckProofAccount - Account DOES NOT EXIST, will create");
                    _logger.Log(LogLevel.Info, $"❌ Proof account does not exist: {_proofAccount}");
                    if (!accountInfo.WasSuccessful)
                    {
                        _logger.Log(LogLevel.Debug, $"RPC call failed: {accountInfo.Reason}");
                    }
                }
                
                return _proofAccountExists;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: CheckProofAccount - Exception: {ex.Message}");
                _logger.Log(LogLevel.Warn, $"Error checking proof account: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CreateProofAccountAsync()
        {
            try
            {
                Console.WriteLine("DEBUG: CreateProofAccountAsync started");
                
                if (_wallet == null || _rpcClient == null)
                {
                    Console.WriteLine("DEBUG: Wallet or RPC client is null");
                    _logger.Log(LogLevel.Error, "CreateProofAccount: Wallet or RPC client is null");
                    return false;
                }

                Console.WriteLine($"DEBUG: Creating proof account for wallet: {_wallet.Account.PublicKey}");
                Console.WriteLine($"DEBUG: Proof account address: {_proofAccount}");
                
                _logger.Log(LogLevel.Info, $"Creating Bitz proof account...");
                _logger.Log(LogLevel.Debug, $"Wallet: {_wallet.Account.PublicKey}");
                _logger.Log(LogLevel.Debug, $"Proof Account: {_proofAccount}");

                // Check ETH balance first since Eclipse uses ETH for gas fees
                Console.WriteLine("DEBUG: Checking ETH balance...");
                _logger.Log(LogLevel.Debug, "Checking ETH balance for gas fees...");
                var balanceResult = await _rpcClient.GetBalanceAsync(_wallet.Account.PublicKey);
                
                Console.WriteLine($"DEBUG: Balance check result: {balanceResult.WasSuccessful}");
                
                if (balanceResult.WasSuccessful)
                {
                    var ethBalance = balanceResult.Result.Value / 1_000_000_000.0; // Convert lamports to ETH
                    Console.WriteLine($"DEBUG: ETH Balance: {ethBalance:0.000000000} ETH");
                    _logger.Log(LogLevel.Info, $"ETH Balance: {ethBalance:0.000000000} ETH");
                    
                    if (ethBalance < 0.00005) // Need at least 0.0005 ETH for transaction fees (reduced from 0.001)
                    {
                        Console.WriteLine($"DEBUG: Insufficient ETH balance: {ethBalance:0.000000000} ETH");
                        _logger.Log(LogLevel.Error, $"❌ Insufficient ETH balance for transaction fees. Need at least 0.0005 ETH, have {ethBalance:0.000000000} ETH");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG: Could not check ETH balance: {balanceResult.Reason}");
                    _logger.Log(LogLevel.Warn, $"⚠️ Could not check ETH balance: {balanceResult.Reason}");
                }

                Console.WriteLine("DEBUG: Creating register instruction...");
                // Create register instruction to initialize proof account using BITZ PROGRAM ID
                var registerInstruction = BitzProgram.Register(
                    BitzProgram.ProgramId, // BITZ Program ID: EorefDWqzJK31vLxaqkDGsx3CRKqPVpWfuJL7qBQMZYd
                    _wallet.Account.PublicKey,
                    _wallet.Account.PublicKey, // miner authority is same as signer for solo
                    _wallet.Account.PublicKey, // funding wallet is same as signer
                    SystemProgram.ProgramIdKey,
                    new PublicKey("SysvarS1otHashes111111111111111111111111111") // Solana sysvar (same across all networks)
                );

                Console.WriteLine("DEBUG: Getting latest blockhash...");
                _logger.Log(LogLevel.Info, $"Created register instruction for Bitz program: {BitzProgram.ProgramId}");

                // Get recent blockhash from ECLIPSE blockchain using the correct method name
                _logger.Log(LogLevel.Debug, "Fetching latest blockhash from Eclipse...");
                var recentBlockhash = await _rpcClient.GetLatestBlockHashAsync();
                
                Console.WriteLine($"DEBUG: Blockhash result: {recentBlockhash.WasSuccessful}");
                
                if (!recentBlockhash.WasSuccessful)
                {
                    Console.WriteLine($"DEBUG: Failed to get blockhash: {recentBlockhash.Reason}");
                    _logger.Log(LogLevel.Error, $"Failed to get recent blockhash from Eclipse: {recentBlockhash.Reason} - {recentBlockhash.HttpStatusCode}");
                    return false;
                }

                Console.WriteLine($"DEBUG: Got blockhash: {recentBlockhash.Result.Value.Blockhash}");
                _logger.Log(LogLevel.Debug, $"Got blockhash: {recentBlockhash.Result.Value.Blockhash}");

                Console.WriteLine("DEBUG: Building transaction...");
                // Create transaction using TransactionBuilder (the proper way)
                var transactionBuilder = new TransactionBuilder()
                    .SetRecentBlockHash(recentBlockhash.Result.Value.Blockhash)
                    .SetFeePayer(_wallet.Account.PublicKey)
                    .AddInstruction(registerInstruction);

                _logger.Log(LogLevel.Debug, "Building and signing transaction...");

                // Build and sign transaction
                var transactionBytes = transactionBuilder.Build(_wallet.Account);

                Console.WriteLine($"DEBUG: Transaction built, size: {transactionBytes.Length} bytes");
                _logger.Log(LogLevel.Debug, $"Transaction built, size: {transactionBytes.Length} bytes");

                Console.WriteLine("DEBUG: Submitting transaction to Eclipse...");
                // Submit transaction to ECLIPSE blockchain
                _logger.Log(LogLevel.Info, "Submitting proof account creation transaction to Eclipse...");
                var result = await _rpcClient.SendTransactionAsync(transactionBytes);
                
                Console.WriteLine($"DEBUG: Transaction submission result: {result.WasSuccessful}");
                
                if (result.WasSuccessful)
                {
                    Console.WriteLine($"DEBUG: SUCCESS! Transaction: {result.Result}");
                    _logger.Log(LogLevel.Info, $"✅ Bitz proof account created on Eclipse! Tx: {result.Result}");
                    _proofAccountExists = true;
                    return true;
                }
                else
                {
                    Console.WriteLine($"DEBUG: FAILED! Reason: {result.Reason}");
                    Console.WriteLine($"DEBUG: HTTP Status: {result.HttpStatusCode}");
                    _logger.Log(LogLevel.Error, $"❌ Failed to create Bitz proof account: {result.Reason}");
                    _logger.Log(LogLevel.Error, $"HTTP Status: {result.HttpStatusCode}");
                    if (result.ErrorData != null)
                    {
                        Console.WriteLine($"DEBUG: Error Code: {result.ServerErrorCode}");
                        Console.WriteLine($"DEBUG: Error Data: {result.ErrorData}");
                        _logger.Log(LogLevel.Error, $"Server Error Code: {result.ServerErrorCode}");
                        _logger.Log(LogLevel.Error, $"Error Data: {result.ErrorData}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: EXCEPTION in CreateProofAccountAsync: {ex.Message}");
                Console.WriteLine($"DEBUG: Stack trace: {ex.StackTrace}");
                _logger.Log(LogLevel.Error, $"❌ Exception creating Bitz proof account: {ex.Message}");
                _logger.Log(LogLevel.Debug, $"Stack trace: {ex.StackTrace}");
                return false;
            }
       }

       private async void GenerateNewChallenge()
       {
            try
            {
                // Fetch REAL challenge from Bitz program on Eclipse blockchain
                await FetchRealChallengeAsync();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error fetching real challenge: {ex.Message}");
            }
        }

        private async Task FetchRealChallengeAsync()
        {
            try
            {
                if (_rpcClient == null)
                {
                    _logger.Log(LogLevel.Warn, "RPC client not initialized, cannot fetch challenge");
                    return;
                }

                // Fetch Bitz config account data to get the real challenge
                var configResult = await _rpcClient.GetAccountInfoAsync(BitzProgram.ConfigAddress);
                
                if (!configResult.WasSuccessful || configResult.Result?.Value?.Data == null)
                {
                    _logger.Log(LogLevel.Warn, $"Failed to fetch Bitz config account: {configResult.Reason}");
                    return;
                }

                var configData = configResult.Result.Value.Data;
                if (configData.Count < 40) // Need at least 40 bytes for challenge
                {
                    _logger.Log(LogLevel.Warn, $"Config data too small: {configData.Count} bytes");
                    return;
                }

                // Extract real challenge from config data (usually starts at offset 8)
                var realChallenge = new byte[32];
                for (int i = 0; i < 32; i++)
                {
                    realChallenge[i] = configData[8 + i]; // Challenge typically at offset 8
                }

                _challengeId++;
                _currentChallenge = realChallenge;

                OnChallengeUpdate?.Invoke(this, new NewChallengeInfo
                {
                    ChallengeId = _challengeId,
                    Challenge = _currentChallenge,
                    StartNonce = 0,
                    EndNonce = ulong.MaxValue,
                    TotalCPUNonces = ulong.MaxValue / 2 
                });

                _logger.Log(LogLevel.Info, $"✅ Fetched REAL challenge from Eclipse blockchain. Challenge ID: {_challengeId}");
                _logger.Log(LogLevel.Debug, $"Challenge: {Convert.ToHexString(_currentChallenge)}");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Exception fetching real challenge: {ex.Message}");
            }
        }

        private async Task SubmitSolutionAsync(DifficultyInfo info)
        {
            if (_wallet == null || _rpcClient == null || !_proofAccountExists)
                return;

            try
            {
                // Get a random bus for submission (BITZ buses, not ORE)
                var bus = BitzProgram.BusIds[new Random().Next(BitzProgram.BusIds.Length)];
                
                _logger.Log(LogLevel.Debug, $"Submitting solution to Bitz bus: {bus} (Program: {BitzProgram.ProgramId})");
                
                // Create mine transaction using BITZ PROGRAM ID
                Console.WriteLine($"DEBUG: Creating mine instruction with accounts:");
                Console.WriteLine($"DEBUG: - Signer: {_wallet.Account.PublicKey}");
                Console.WriteLine($"DEBUG: - Bus: {bus}");
                Console.WriteLine($"DEBUG: - Proof: {_proofAccount}");
                Console.WriteLine($"DEBUG: - Solution length: {info.BestSolution.Length}");
                Console.WriteLine($"DEBUG: - Nonce: {info.BestNonce}");
                
                var mineInstruction = BitzProgram.Mine(
                    BitzProgram.ProgramId, // BITZ Program ID: EorefDWqzJK31vLxaqkDGsx3CRKqPVpWfuJL7qBQMZYd (NOT ORE)
                    _wallet.Account.PublicKey,
                    bus, // Bitz bus
                    _proofAccount, // Bitz proof account
                    info.BestSolution,
                    info.BestNonce
                );

                // Add Auth instruction (like ORE does)
                var authInstruction = BitzProgram.Auth(_proofAccount);
                
                Console.WriteLine($"DEBUG: Mine instruction accounts: {mineInstruction.Keys.Count}");
                Console.WriteLine($"DEBUG: Auth instruction accounts: {authInstruction.Keys.Count}");

                // Get recent blockhash from ECLIPSE blockchain (not Solana) using correct method
                var recentBlockhash = await _rpcClient.GetLatestBlockHashAsync();
                if (!recentBlockhash.WasSuccessful)
                {
                    _logger.Log(LogLevel.Warn, "Failed to get blockhash from Eclipse for solution submission");
                    return;
                }

                // Create transaction using TransactionBuilder (the proper way)
                var transactionBuilder = new TransactionBuilder()
                    .SetRecentBlockHash(recentBlockhash.Result.Value.Blockhash)
                    .SetFeePayer(_wallet.Account.PublicKey)
                    .AddInstruction(authInstruction)  // Add auth instruction first
                    .AddInstruction(mineInstruction); // Then mine instruction

                // Build and sign transaction
                var transactionBytes = transactionBuilder.Build(_wallet.Account);

                // Submit transaction to ECLIPSE blockchain (not Solana mainnet)
                var result = await _rpcClient.SendTransactionAsync(transactionBytes);
                
                if (result.WasSuccessful)
                {
                    _logger.Log(LogLevel.Info, $"Bitz solution submitted to Eclipse! Difficulty: {info.BestDifficulty}, Tx: {result.Result}");
                    // Note: Actual reward calculation would need to be fetched from Eclipse blockchain
                    _miningRewards += 1.0; // Placeholder
                }
                else
                {
                    _logger.Log(LogLevel.Warn, $"Failed to submit Bitz solution to Eclipse: {result.Reason}");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error submitting Bitz solution: {ex.Message}");
            }
        }

        private async Task UpdateWalletBalanceAsync()
        {
            try
            {
                if (_wallet == null || _rpcClient == null)
                    return;

                // Get token account for BITZ token (not ORE) on Eclipse blockchain
                var tokenAccounts = await _rpcClient.GetTokenAccountsByOwnerAsync(
                    _wallet.Account.PublicKey,
                    BitzProgram.MintId.Key // BITZ Token Mint: 64mggk2nXg6vHC1qCdsZdEFzd5QGN4id54Vbho4PswCF (NOT ORE)
                );

                if (tokenAccounts.WasSuccessful && tokenAccounts.Result?.Value?.Count > 0)
                {
                    var tokenAccount = tokenAccounts.Result.Value.FirstOrDefault();
                    if (tokenAccount?.Account?.Data?.Parsed != null)
                    {
                        // Parse token account data using the correct ParsedTokenAccountData structure
                        var tokenAccountInfo = tokenAccount.Account.Data.Parsed.Info;
                        if (tokenAccountInfo?.TokenAmount != null)
                        {
                            var balance = tokenAccountInfo.TokenAmount.Amount;
                            
                            if (ulong.TryParse(balance, out ulong balanceRaw))
                            {
                                var newBalance = balanceRaw / BitzProgram.BitzDecimals; // Using Bitz decimals
                                if (Math.Abs(_walletBalance - newBalance) > 0.000001) // Only log if changed
                                {
                                    _logger.Log(LogLevel.Debug, $"Bitz wallet balance updated: {newBalance} BITZ");
                                    _walletBalance = newBalance;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, $"Error updating Bitz wallet balance: {ex.Message}");
            }
        }

        private async Task LogOnChainDataAsync()
        {
            try
            {
                Console.WriteLine("=== ON-CHAIN DEBUG DATA ===");
                
                // 1. Check Bitz Config Account
                Console.WriteLine($"DEBUG: Fetching Bitz Config Account: {BitzProgram.ConfigAddress}");
                var configInfo = await _rpcClient.GetAccountInfoAsync(BitzProgram.ConfigAddress);
                if (configInfo.WasSuccessful && configInfo.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Config Account - Owner: {configInfo.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Config Account - Lamports: {configInfo.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Config Account - Data Length: {configInfo.Result.Value.Data?.Count ?? 0}");
                    Console.WriteLine($"DEBUG: Config Account - Executable: {configInfo.Result.Value.Executable}");
                }
                else
                {
                    Console.WriteLine($"DEBUG: Config Account - NOT FOUND or RPC failed: {configInfo.Reason}");
                }

                // 2. Check our Proof Account details
                Console.WriteLine($"DEBUG: Fetching Proof Account: {_proofAccount}");
                var proofInfo = await _rpcClient.GetAccountInfoAsync(_proofAccount);
                if (proofInfo.WasSuccessful && proofInfo.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Proof Account - Owner: {proofInfo.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Proof Account - Lamports: {proofInfo.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Proof Account - Data Length: {proofInfo.Result.Value.Data?.Count ?? 0}");
                    Console.WriteLine($"DEBUG: Proof Account - Executable: {proofInfo.Result.Value.Executable}");
                    
                    // If data exists, show first few bytes
                    if (proofInfo.Result.Value.Data?.Count > 0)
                    {
                        Console.WriteLine($"DEBUG: Proof Account - First Data Element: {proofInfo.Result.Value.Data[0]}");
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG: Proof Account - NOT FOUND or RPC failed: {proofInfo.Reason}");
                }

                // 3. Check Bitz Program Account
                Console.WriteLine($"DEBUG: Fetching Bitz Program: {BitzProgram.ProgramId}");
                var programInfo = await _rpcClient.GetAccountInfoAsync(BitzProgram.ProgramId);
                if (programInfo.WasSuccessful && programInfo.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Bitz Program - Owner: {programInfo.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Bitz Program - Lamports: {programInfo.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Bitz Program - Executable: {programInfo.Result.Value.Executable}");
                }
                else
                {
                    Console.WriteLine($"DEBUG: Bitz Program - NOT FOUND or RPC failed: {programInfo.Reason}");
                }

                // 4. Check a random Bus Account
                var randomBus = BitzProgram.BusIds[0];
                Console.WriteLine($"DEBUG: Fetching Bus Account: {randomBus}");
                var busInfo = await _rpcClient.GetAccountInfoAsync(randomBus);
                if (busInfo.WasSuccessful && busInfo.Result?.Value != null)
                {
                    Console.WriteLine($"DEBUG: Bus Account - Owner: {busInfo.Result.Value.Owner}");
                    Console.WriteLine($"DEBUG: Bus Account - Lamports: {busInfo.Result.Value.Lamports}");
                    Console.WriteLine($"DEBUG: Bus Account - Data Length: {busInfo.Result.Value.Data?.Count ?? 0}");
                }
                else
                {
                    Console.WriteLine($"DEBUG: Bus Account - NOT FOUND or RPC failed: {busInfo.Reason}");
                }

                Console.WriteLine("=== END ON-CHAIN DEBUG ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Error fetching on-chain data: {ex.Message}");
            }
       }
   }
}