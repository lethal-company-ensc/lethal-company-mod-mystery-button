using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using Random = System.Random;

namespace MysteryButton.Scripts
{
    public class MysteryButtonAI : EnemyAI, INetworkSerializable
    {
        private static int _cpt = 0;

        private const bool IsTest = false;
        private static NetworkVariable<int> _testIndexEffect = new ();

        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(
            "MysteryButton.MysteryButtonAI"
        );

        private static readonly int PlayUsed = Animator.StringToHash("playUsed");
        
        private static readonly int PlayDestroyed = Animator.StringToHash("playDestroyed");
        
        private static readonly int PlayIdleBouncing = Animator.StringToHash("playIdleBouncing");
        
        private static Dictionary<EffectType, int> GoodBadEffects;
        private static int GoodBadEffectsWeightSum;
        
        private static Dictionary<GoodEffectType, int> GoodEffects;
        private static int GoodEffectsWeightSum;
        private static Dictionary<GoodEffectType, bool> GoodEffectCondition;
        
        private static Dictionary<BadEffectType, int> BadEffects;
        private static int BadEffectsWeightSum;
        private static Dictionary<BadEffectType, bool> BadEffectCondition;

        private Random _rng;

        private NetworkVariable<bool> _isLock = new ();

        private bool _isLocalLock;

        private int _id;

        private AudioClip _buttonAppearClip;

        private AudioClip _buttonUsedClip;

        private AudioClip _buttonUsedBadClip;

        private AudioClip _teleporterBeamClip;

        private List<AudioClip> _playerBadEffectClips;

        private bool _canExplodeLandmines;

        private bool _canMakeTurretsBerserk;

        private bool _canOpenSteamValveHazard;

        private bool _canTurnOffLights;

        private EnemyVent _nearestVent;

        private Animator _animator;

        public override void Start()
        {
            Logger.LogDebug("Start");
            _isLocalLock = false;

            _animator = gameObject.GetComponentInChildren<Animator>();
            
            float waitTime = 10f;
            InvokeRepeating ("PlayIdleAnimation", 10f, waitTime);

            AudioSource audioSource = gameObject.GetComponent<AudioSource>();
            Logger.LogDebug("AudioSource is " + (audioSource ? " not null" : "null"));
            creatureSFX = gameObject.GetComponent<AudioSource>();

            AudioClip[] audioClips = enemyType?.audioClips ?? [];
            _buttonAppearClip = audioClips[0];
            _buttonUsedClip = audioClips[1];
            _buttonUsedBadClip = audioClips[2];
            _playerBadEffectClips = [audioClips[3], audioClips[4]];
            _teleporterBeamClip = audioClips[5];

            _id = _cpt++;
            enemyHP = 100;
            _rng = new Random((int)NetworkObjectId);

            if (creatureSFX)
            {
                creatureSFX.PlayOneShot(_buttonAppearClip);
            }

            List<Landmine> landmines = FindObjectsOfType<Landmine>()
                .Where(mine => !mine.hasExploded)
                .ToList();
            _canExplodeLandmines = landmines.Count > 0;
            
            List<SteamValveHazard> steamValves = FindObjectsOfType<SteamValveHazard>().ToList();
            _canOpenSteamValveHazard = steamValves.Count > 0;
            
            List<Turret> turrets = FindObjectsOfType<Turret>().ToList();
            _canMakeTurretsBerserk = turrets.Count > 0;
            
            List<EnemyVent> vents = RoundManager.Instance.allEnemyVents.ToList();

            if (vents.Count > 0)
            {
                _nearestVent = vents[0];

                foreach (EnemyVent vent in vents)
                {
                    float distBetweenNearestVentAndButton =
                        Vector3.Distance(_nearestVent.transform.position, transform.position);
                    float distBetweenVentAndButton = Vector3.Distance(vent.transform.position, transform.position);
                    if (distBetweenNearestVentAndButton > distBetweenVentAndButton)
                    {
                        _nearestVent = vent;
                    }
                }
            }
            
            GoodBadEffects = ConfigEffectParsing<EffectType>(MysteryButtonConfig.ConfigEffects.Value);
            GoodBadEffectsWeightSum = GoodBadEffects.Sum(effect => effect.Value);
        
            GoodEffects = ConfigEffectParsing<GoodEffectType>(MysteryButtonConfig.ConfigGoodEffects.Value);
            GoodEffectsWeightSum = GoodEffects.Sum(effect => effect.Value);
        
            BadEffects = ConfigEffectParsing<BadEffectType>(MysteryButtonConfig.ConfigBadEffects.Value);
            BadEffectsWeightSum = BadEffects.Sum(effect => effect.Value);
            
            GoodEffectCondition = new Dictionary<GoodEffectType, bool>
            {
                [GoodEffectType.ExplodeLandmines] = _canExplodeLandmines
            };
            
            BadEffectCondition = new Dictionary<BadEffectType, bool>
            {
                [BadEffectType.OpenAllSteamValveHazard] = _canOpenSteamValveHazard,
                [BadEffectType.BerserkTurrets] =  _canMakeTurretsBerserk,
                [BadEffectType.TurnOffLights] = _canTurnOffLights
            };

            base.Start();
        }

        private void PlayIdleAnimation()
        {
            if (_animator && !_isLocalLock && !_isLock.Value)
            {
                _animator.SetTrigger(PlayIdleBouncing);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Logger.LogDebug("OnNetworkSpawn, IsServer=" + IsServer);
            if (IsServer)
            {
                _isLock.Value = false;
                NetworkManager.OnClientConnectedCallback += NetworkManager_OnClientConnectedCallback;
            }
            else
            {
                _isLock.OnValueChanged += OnIsLockChanged;

                if (IsTest && _id == 0)
                {
                    _testIndexEffect.OnValueChanged += OnTestIndexChanged;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsServer)
            {
                _isLock.OnValueChanged -= OnIsLockChanged;
            }
        }

        private void NetworkManager_OnClientConnectedCallback(ulong obj)
        {
            InitNetworkVariables();
        }
        
        private void OnIsLockChanged(bool previous, bool current)
        {
            Logger.LogDebug($"Detected NetworkVariable isLock Change: Previous: {previous} | Current: {current}");
            _isLock.Value = current;
        }
        
        private void OnTestIndexChanged(int previous, int current)
        {
            Logger.LogDebug($"Detected NetworkVariable testIndexEffect Change: Previous: {previous} | Current: {current}");
            _testIndexEffect.Value = current;
        }

        private void InitNetworkVariables()
        {
            _isLock.Value = false;
            NetworkManager.OnClientConnectedCallback -= NetworkManager_OnClientConnectedCallback;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetLockServerRpc()
        {
            Logger.LogDebug("SetLock");
            _isLock.Value = true;
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void IncrementTestIndexEffectServerRpc()
        {
            Logger.LogDebug("IncrementTestIndexEffect");
            if (_testIndexEffect.Value > 18)
            {
                _testIndexEffect.Value = 0;
            }
            else
            {
                _testIndexEffect.Value += 1;
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (!_isLocalLock && !_isLock.Value)
            {
                BreakerBox breakerBox = FindObjectOfType<BreakerBox>();
                _canTurnOffLights = breakerBox && breakerBox.isPowerOn;
                
                _isLocalLock = true;
                Logger.LogDebug("OnCollideWithPlayer, id=" + _id);
                SetLockServerRpc();
                PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();

                if (player != null)
                {
                    if (IsTest)
                    {
                        DoTestEffect(player.name);
                    }
                    else
                    {
                        DoEffect(player.name);
                    }
                }
            }
        }

        public void DoEffect(string playerName)
        {
            int effect = _rng.Next(0, GoodBadEffectsWeightSum);
            bool isGood = effect < GoodBadEffects.GetValueOrDefault(EffectType.Good, 50);

            if (isGood)
            {
                DoGoodEffect(playerName);
            }
            else
            {
                DoBadEffect(playerName);
            }

            KillButtonServerRpc(isGood);
        }

        private void DoTestEffect(string playerName)
        {
            Logger.LogDebug("TestEffect with index=" + _testIndexEffect.Value);
            switch (_testIndexEffect.Value)
            {
                case 0:
                    SpawnScrapServerRpc();
                    break;
                case 1:
                    SpawnScrapServerRpc(_rng.Next(1, 6));
                    break;
                case 2:
                    SpawnExpensiveScrapServerRpc(1);
                    break;
                case 3:
                    SpawnExpensiveScrapServerRpc(_rng.Next(1, 11));
                    break;
                case 4:
                    ExplodeLandminesServerRpc();
                    break;
                case 5:
                    PlayerControllerB randomPlayer = GetActivePlayers()
                        .Where((player) => !player.isPlayerDead)
                        .Where((player) => player.isPlayerControlled)
                        .First();
                    RevivePlayerServerRpc(randomPlayer.playerUsername);
                    break;
                case 6:
                    StartMeteorEventServerRpc();
                    break;
                case 7:
                    TeleportPlayerToRandomPositionServerRpc(playerName);
                    break;
                case 8:
                    SwitchPlayersPositionServerRpc(playerName);
                    break;
                case 9:
                    OpenAllSteamValveHazardServerRpc();
                    break;
                case 10:
                    PlayerDrunkServerRpc();
                    break;
                case 11:
                    TurnOffLightsServerRpc();
                    break;
                case 12:
                    RandomPlayerIncreaseInsanityServerRpc();
                    break;
                case 13:
                    BerserkTurretServerRpc();
                    break;
                case 14:
                    SpawnEnemyServerRpc(1);
                    break;
                case 15:
                    SpawnEnemyServerRpc(_rng.Next(1, 5));
                    break;
                case 16:
                    OpenAllDoorsServerRpc(playerName);
                    break;
                case 17:
                    CloseAllDoorsServerRpc(playerName);
                    break;
                case 18:
                    LeaveEarlyServerRpc();
                break;
            }

            IncrementTestIndexEffectServerRpc();
            KillButtonServerRpc(_testIndexEffect.Value < 7);
        }

        private void DoGoodEffect(string playerName)
        {
            GoodEffectType? selectedEffect = GetRandomEffect(_rng, GoodEffectsWeightSum, GoodEffects, GoodEffectCondition);
            Logger.LogInfo("Good effect=" + selectedEffect);

            switch (selectedEffect)
            {
                case GoodEffectType.SpawnOneScrap:
                    SpawnScrapServerRpc();
                    break;
                case GoodEffectType.SpawnMultipleScrap:
                    SpawnScrapServerRpc(_rng.Next(1, 6));
                    break;
                case GoodEffectType.SpawnOneExpensiveScrap:
                    SpawnExpensiveScrapServerRpc(1);
                    break;
                case GoodEffectType.SpawnMultipleExpensiveScrap:
                    SpawnExpensiveScrapServerRpc(_rng.Next(1, 11));
                    break;
                case GoodEffectType.ExplodeLandmines:
                    ExplodeLandminesServerRpc();
                    break;
                case GoodEffectType.RevivePlayer:
                    RevivePlayerServerRpc(playerName);
                    break;
                default:
                    SpawnScrapServerRpc();
                    break;
            }
        }

        private void DoBadEffect(string playerName)
        {
            BadEffectType? selectedEffect = GetRandomEffect(_rng, BadEffectsWeightSum, BadEffects, BadEffectCondition);
            Logger.LogInfo("Bad effect=" + selectedEffect);

            switch (selectedEffect) {
                case BadEffectType.StartMeteorShower:
                    StartMeteorEventServerRpc();
                    break;
                case BadEffectType.TeleportPlayerToRandomPosition:
                    TeleportPlayerToRandomPositionServerRpc(playerName);
                    break;
                case BadEffectType.SwitchPlayersPosition:
                    SwitchPlayersPositionServerRpc(playerName);
                    break;
                case BadEffectType.OpenAllSteamValveHazard:
                    OpenAllSteamValveHazardServerRpc();
                    break;
                case BadEffectType.PlayerDrunkEffect:
                    PlayerDrunkServerRpc();
                    break;
                case BadEffectType.LeaveEarly:
                    LeaveEarlyServerRpc();
                    break;
                case BadEffectType.RandomPlayerIncreaseInsanity:
                    RandomPlayerIncreaseInsanityServerRpc();
                    break;
                case BadEffectType.BerserkTurrets:
                    BerserkTurretServerRpc();
                    break;
                case BadEffectType.SpawnOneEnemy:
                    SpawnEnemyServerRpc(1);
                    break;
                case BadEffectType.SpawnMultipleEnemies:
                    SpawnEnemyServerRpc(_rng.Next(1, 5));
                    break;
                case BadEffectType.TurnOffLights:
                    TurnOffLightsServerRpc();
                    break;
                case BadEffectType.OpenCloseDoors:
                    int open = _rng.Next(0, 100);
                    if (open < 50)
                    {
                        OpenAllDoorsServerRpc(playerName);
                    }
                    else
                    {
                        CloseAllDoorsServerRpc(playerName);
                    }
                    break;
                default:
                    TeleportPlayerToRandomPositionServerRpc(playerName);
                    break;
            }
        }

        private T? GetRandomEffect<T>(Random rng, int weightSum, Dictionary<T, int> weightDict, Dictionary<T, bool> conditions) where T : struct
        {
            int effect = rng.Next(0, weightSum);

            T? selectedEffect = null;
            int currentValue = 0;
            foreach (var item in weightDict)
            {
                currentValue += item.Value;
                Logger.LogInfo("currentValue=" + currentValue + ", key=" + item.Key + " / value=" + item.Value);
                if (effect < currentValue && (!conditions.ContainsKey(item.Key) || conditions[item.Key]))
                {
                    selectedEffect = item.Key;
                    break;
                }
            }

            if (selectedEffect == null)
            {
                Logger.LogInfo("No item selected, choosing random one");
                var reducedList = weightDict.Keys.Except(conditions.Keys).ToList();
                selectedEffect = reducedList[rng.Next(0, reducedList.Count())];
            }
            return selectedEffect;
        }

        #region KillButton
        [ServerRpc(RequireOwnership = false)]
        public void KillButtonServerRpc(bool isGood)
        {
            KillButtonClientRpc(isGood);
        }

        [ClientRpc]
        public void KillButtonClientRpc(bool isGood)
        {
            Logger.LogDebug("KillButtonClientRpc");
            if (creatureSFX)
            {
                creatureSFX.Stop();
                if (isGood)
                {
                    creatureSFX.PlayOneShot(_buttonUsedClip);
                }
                else
                {
                    creatureSFX.PlayOneShot(_buttonUsedBadClip);
                }
            }

            if (isGood) 
            {
                Material buttonUsedMaterial = MysteryButton.buttonUsedMaterial;
                transform.Find("MysteryButton/SpringBones/Bone.004/MysteryButton_Bouton").GetComponent<MeshRenderer>().material = buttonUsedMaterial;
            }

            if (_animator)
            {
                if (isGood)
                {
                    _animator.SetBool(PlayUsed, true);
                }
                else
                {
                    _animator.SetBool(PlayDestroyed, true);
                }
            }
            KillEnemy();
        }
        #endregion KillEnemy

        #region PlayerDrunk
        [ServerRpc(RequireOwnership = false)]
        void PlayerDrunkServerRpc()
        {
            PlayerDrunkClientRpc();
        }

        [ClientRpc]
        void PlayerDrunkClientRpc()
        {
            Logger.LogDebug("PlayerDrunkClientRpc");
            if (StartOfRound.Instance != null)
            {
                foreach (PlayerControllerB player in GetActivePlayers())
                {
                    Logger.LogDebug("Client: Apply effect to " + player.playerUsername);
                    player.drunkness = 5f;
                }
            }
        }
        #endregion PlayerDrunk

        #region RevivePlayer
        [ServerRpc(RequireOwnership = false)]
        void RevivePlayerServerRpc(string? playerName)
        {
            Logger.LogDebug("ButtonAI:RevivePlayerServerRpc");
            var player = GetPlayerByNameOrFirstOne(playerName);
            var deadPlayers = StartOfRound
                .Instance.allPlayerScripts.Where((p) => p.isPlayerDead)
                .ToList();
            if (deadPlayers.Count > 0)
            {
                var deadPlayer = deadPlayers[_rng.Next(0, deadPlayers.Count)];
                RevivePlayerClientRpc(playerName, deadPlayer.name);
                TeleportPlayerToPositionClientRpc(deadPlayer.name, player.transform.position);
            }
        }

        [ClientRpc]
        void RevivePlayerClientRpc(string? playerName, string? deadPlayerName)
        {
            Logger.LogDebug("ButtonAI:RevivePlayerClientRpc");
            StartOfRound instance = StartOfRound.Instance;
            var player = GetPlayerByNameOrFirstOne(playerName);
            var deadPlayer = GetPlayerByNameOrFirstOne(deadPlayerName);
            var deadPlayerIndex = instance.allPlayerScripts.IndexOf(p => p.name == deadPlayer.name);

            Logger.LogDebug(
                "Client: Trying to revive "
                    + deadPlayer.playerUsername
                    + " with index="
                    + deadPlayerIndex
            );
            int health = 100;
            deadPlayer.ResetPlayerBloodObjects(deadPlayer.isPlayerDead);

            if (deadPlayer.isPlayerDead || deadPlayer.isPlayerControlled)
            {
                deadPlayer.isClimbingLadder = false;
                deadPlayer.inVehicleAnimation = false;
                deadPlayer.ResetZAndXRotation();
                deadPlayer.thisController.enabled = true;
                deadPlayer.health = health;
                deadPlayer.disableLookInput = false;

                if (deadPlayer.isPlayerDead)
                {
                    deadPlayer.isPlayerDead = false;
                    deadPlayer.isPlayerControlled = true;
                    deadPlayer.isInElevator = false;
                    deadPlayer.isInHangarShipRoom = false;
                    deadPlayer.isInsideFactory = player.isInsideFactory;
                    StartOfRound.Instance.SetPlayerObjectExtrapolate(false);
                    deadPlayer.setPositionOfDeadPlayer = false;
                    deadPlayer.helmetLight.enabled = false;
                    deadPlayer.Crouch(false);
                    deadPlayer.criticallyInjured = false;

                    if (deadPlayer.playerBodyAnimator != null)
                    {
                        deadPlayer.playerBodyAnimator.SetBool("Limp", false);
                    }

                    deadPlayer.bleedingHeavily = false;
                    deadPlayer.activatingItem = false;
                    deadPlayer.twoHanded = false;
                    deadPlayer.inSpecialInteractAnimation = false;
                    deadPlayer.disableSyncInAnimation = false;
                    deadPlayer.inAnimationWithEnemy = null;
                    deadPlayer.holdingWalkieTalkie = false;
                    deadPlayer.speakingToWalkieTalkie = false;
                    deadPlayer.isSinking = false;
                    deadPlayer.isUnderwater = false;
                    deadPlayer.sinkingValue = 0f;
                    deadPlayer.statusEffectAudio.Stop();
                    deadPlayer.DisableJetpackControlsLocally();
                    deadPlayer.health = health;
                    deadPlayer.mapRadarDotAnimator.SetBool("dead", false);
                    deadPlayer.deadBody = null;

                    if (deadPlayer == GameNetworkManager.Instance.localPlayerController)
                    {
                        HUDManager.Instance.gasHelmetAnimator.SetBool("gasEmitting", false);
                        deadPlayer.hasBegunSpectating = false;
                        HUDManager.Instance.RemoveSpectateUI();
                        HUDManager.Instance.gameOverAnimator.SetTrigger("revive");
                        deadPlayer.hinderedMultiplier = 1f;
                        deadPlayer.isMovementHindered = 0;
                        deadPlayer.sourcesCausingSinking = 0;
                        HUDManager.Instance.HideHUD(false);
                    }
                }

                SoundManager.Instance.earsRingingTimer = 0f;
                deadPlayer.voiceMuffledByEnemy = false;

                if (deadPlayer.currentVoiceChatIngameSettings == null)
                {
                    StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                }

                if (deadPlayer.currentVoiceChatIngameSettings != null)
                {
                    if (deadPlayer.currentVoiceChatIngameSettings.voiceAudio == null)
                    {
                        deadPlayer.currentVoiceChatIngameSettings.InitializeComponents();
                    }

                    if (deadPlayer.currentVoiceChatIngameSettings.voiceAudio != null)
                    {
                        deadPlayer
                            .currentVoiceChatIngameSettings.voiceAudio.GetComponent<OccludeAudio>()
                            .overridingLowPass = false;
                    }
                }
            }

            StartOfRound.Instance.livingPlayers++;
            if (GameNetworkManager.Instance.localPlayerController == deadPlayer)
            {
                deadPlayer.bleedingHeavily = false;
                deadPlayer.criticallyInjured = false;
                deadPlayer.playerBodyAnimator?.SetBool("Limp", false);
                deadPlayer.health = health;
                HUDManager.Instance.UpdateHealthUI(health, false);
                deadPlayer.spectatedPlayerScript = null;
                HUDManager.Instance.audioListenerLowPass.enabled = false;
                StartOfRound.Instance.SetSpectateCameraToGameOverMode(false, deadPlayer);
                TimeOfDay.Instance.DisableAllWeather(false);
                StartOfRound.Instance.UpdatePlayerVoiceEffects();
                deadPlayer.thisPlayerModel.enabled = true;
            }
            else
            {
                deadPlayer.thisPlayerModel.enabled = true;
                deadPlayer.thisPlayerModelLOD1.enabled = true;
                deadPlayer.thisPlayerModelLOD2.enabled = true;
            }
        }
        #endregion RevivePlayer

        #region RandomPlayerIncreaseInsanity
        [ServerRpc(RequireOwnership = false)]
        void RandomPlayerIncreaseInsanityServerRpc()
        {
            RandomPlayerIncreaseInsanityClientRpc();
        }

        [ClientRpc]
        void RandomPlayerIncreaseInsanityClientRpc()
        {
            Logger.LogDebug("RandomPlayerIncreaseInsanityClientRpc");
            if (StartOfRound.Instance != null)
            {
                PlayerControllerB[] currentPlayers = GetActivePlayers()
                    .Where(player => (!player.isPlayerDead || player.isPlayerControlled) && player.playerSteamId != 0)
                    .ToArray();
                PlayerControllerB player = currentPlayers[_rng.Next(currentPlayers.Length)];
                player.insanityLevel = player.maxInsanityLevel;
                player.JumpToFearLevel(1.25F);
                player.movementAudio.PlayOneShot(
                    _playerBadEffectClips[_rng.Next(0, _playerBadEffectClips.Count)]
                );
                player.JumpToFearLevel(1.25f);
                RoundManager.Instance.FlickerLights();
                Logger.LogDebug("Client: Apply max insanity to " + player.playerUsername);
            }
        }
        #endregion RandomPlayerIncreaseInsanity

        #region OpenAllDoors
        [ServerRpc(RequireOwnership = false)]
        void OpenAllDoorsServerRpc(string? entityName)
        {
            OpenAllDoorsClientRpc(entityName);
        }

        [ClientRpc]
        void OpenAllDoorsClientRpc(string? entityName)
        {
            Logger.LogDebug("OpenAllDoorsClientRpc");
            var player = GetPlayerByNameOrFirstOne(entityName);

            List<DoorLock> doors = FindObjectsOfType<DoorLock>().ToList();
            foreach (DoorLock door in doors)
            {
                bool openLockedDoor = !door.isLocked || _rng.Next(0, 10) < 2;
                if (!door.isDoorOpened && openLockedDoor)
                {
                    if (door.isLocked)
                    {
                        Logger.LogDebug("Unlocking door id=" + door.NetworkObjectId);
                        door.isLocked = false;
                        if (door.doorLockSFX && door.unlockSFX)
                        {
                            door.doorLockSFX.PlayOneShot(door.unlockSFX);
                        }
                    }
                    door.OpenOrCloseDoor(player);
                }
            }
        }
        #endregion OpenAllDoors

        #region CloseAllDoors
        [ServerRpc(RequireOwnership = false)]
        void CloseAllDoorsServerRpc(string? entityName)
        {
            CloseAllDoorsClientRpc(entityName);
        }

        [ClientRpc]
        void CloseAllDoorsClientRpc(string? entityName)
        {
            Logger.LogDebug("CloseAllDoorsClientRpc");
            var player = GetPlayerByNameOrFirstOne(entityName);

            List<DoorLock> doors = FindObjectsOfType<DoorLock>().ToList();
            Logger.LogDebug("CloseAllDoors: " + doors.Count);
            foreach (DoorLock door in doors)
            {
                if (door.isDoorOpened)
                {
                    door.OpenOrCloseDoor(player);

                    if (_rng.Next(0, 10) < 2)
                    {
                        Logger.LogDebug("Locking door id=" + door.NetworkObjectId);
                        if (door.doorLockSFX && door.unlockSFX)
                        {
                            door.doorLockSFX.PlayOneShot(door.unlockSFX);
                        }
                        door.isLocked = true;
                    }
                }
            }
        }
        #endregion CloseAllDoors

        #region ExplodeLandmines

        [ServerRpc(RequireOwnership = false)]
        void ExplodeLandminesServerRpc()
        {
            Logger.LogDebug("ExplodeLandminesServerRpc");
            List<Landmine> landmines = FindObjectsOfType<Landmine>()
                .Where(mine => !mine.hasExploded)
                .ToList();
            Logger.LogDebug(landmines.Count + " landmines found");
            foreach (var landmine in landmines)
            {
                Logger.LogDebug("Exploding landmine id=" + landmine.NetworkObjectId);
                landmine.ExplodeMineServerRpc();
            }
        }
        #endregion ExplodeLandmines

        #region SpawnScrap

        [ServerRpc(RequireOwnership = false)]
        void SpawnScrapServerRpc(int amount)
        {
            Logger.LogDebug("SpawnScrapServerRpc");
            SpawnScrap(false, amount);
        }

        [ServerRpc(RequireOwnership = false)]
        void SpawnScrapServerRpc()
        {
            Logger.LogDebug("SpawnScrapServerRpc");
            SpawnScrap(false, 1);
        }

        [ServerRpc(RequireOwnership = false)]
        void SpawnExpensiveScrapServerRpc(int amount)
        {
            Logger.LogDebug("SpawnExpensiveScrapServerRpc");
            SpawnScrap(true, amount);
        }

        void SpawnScrap(bool expensiveScrap, int amount)
        {
            List<Item> allScrapList = StartOfRound
                .Instance.allItemsList.itemsList.Where((item) => item.isScrap && (!expensiveScrap || item.maxValue > 150))
                .ToList();

            for (int i = 0; i < amount; i++)
            {
                Item randomItem = allScrapList[_rng.Next(0, allScrapList.Count)];

                Quaternion randomRot = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                Vector3 position =
                    transform.position
                    + randomRot * Vector3.forward * NextFloat(_rng, 0.3f, 0.6f);

                GameObject obj = Instantiate(randomItem.spawnPrefab, position, Quaternion.identity, StartOfRound.Instance.propsContainer);

                int value = _rng.Next(randomItem.minValue, randomItem.maxValue);

                Logger.LogDebug(
                    "Spawning item=" + randomItem.name + ", value=" + value + ", weight=" + randomItem.weight
                );

                obj.GetComponent<GrabbableObject>().fallTime = 0f;
                obj.GetComponent<GrabbableObject>().SetScrapValue(value);

                var networkObject = obj.GetComponent<NetworkObject>();
                networkObject.Spawn();
                SpawnScrapClientRpc(networkObject.NetworkObjectId, value);
            }
        }

        [ClientRpc]
        void SpawnScrapClientRpc(ulong networkObjectId, int value)
        {
            var networkObject = NetworkManager.SpawnManager.SpawnedObjects[networkObjectId];
            var grabbableObject = networkObject.GetComponent<GrabbableObject>();
            grabbableObject.SetScrapValue(value);
            grabbableObject.fallTime = 0f;
        }

        #endregion SpawnScrap

        #region SpawnEnemy

        [ServerRpc(RequireOwnership = false)]
        void SpawnEnemyServerRpc(int amount)
        {
            Logger.LogDebug("SpawnEnemyServerRpc, amount=" + amount);
            List<SpawnableEnemyWithRarity> enemies = StartOfRound.Instance.currentLevel.Enemies;
            int allEnemiesListSize = enemies.Count;
            
            for (int i = 0; i < amount; i++)
            {
                int allEnemiesListIndex = _rng.Next(0, allEnemiesListSize);
                SpawnableEnemyWithRarity randomEnemy = enemies[allEnemiesListIndex];

                Logger.LogDebug("Spawning enemy=" + randomEnemy.enemyType.name);
                
                GameObject obj = Instantiate(
                    randomEnemy.enemyType.enemyPrefab,
                    _nearestVent.transform.position,
                    Quaternion.identity
                );

                obj.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            }
        }

        #endregion SpawnEnemy

        #region BerserkTurretEnemy

        [ServerRpc(RequireOwnership = false)]
        void BerserkTurretServerRpc()
        {
            BerserkTurretClientRpc();
        }

        [ClientRpc]
        void BerserkTurretClientRpc()
        {
            Logger.LogDebug("BerserkTurretClientRpc");
            
            List<Turret> turrets = FindObjectsOfType<Turret>().ToList();
            
            Logger.LogDebug(turrets.Count + " turrets found");

            foreach (var turret in turrets)
            {
                turret.SwitchTurretMode((int) TurretMode.Berserk);
            }
        }

        #endregion BerserkTurret

        #region TeleportPlayerToRandomPosition

        [ServerRpc(RequireOwnership = false)]
        void TeleportPlayerToRandomPositionServerRpc(string? playerName)
        {
            TeleportPlayerToRandomPositionClientRpc(playerName);
        }

        [ClientRpc]
        void TeleportPlayerToRandomPositionClientRpc(string? playerName)
        {
            Logger.LogDebug("TeleportPlayerToRandomPositionClientRpc");
            PlayerControllerB player = GetPlayerByNameOrFirstOne(playerName);

            int randomIndex = _rng.Next(0, RoundManager.Instance.insideAINodes.Length);
            var teleportPos = RoundManager.Instance.insideAINodes[randomIndex].transform.position;

            if ((bool)(UnityEngine.Object)FindObjectOfType<AudioReverbPresets>())
                FindObjectOfType<AudioReverbPresets>()
                    .audioPresets[2]
                    .ChangeAudioReverbForPlayer(player);
            player.isInElevator = false;
            player.isInHangarShipRoom = false;
            player.isInsideFactory = true;
            player.averageVelocity = 0.0f;
            player.velocityLastFrame = Vector3.zero;
            player.TeleportPlayer(teleportPos);
            player.beamOutParticle.Play();
            player.movementAudio.PlayOneShot(_teleporterBeamClip);
        }

        #endregion TeleportPlayerToRandomPosition

        #region TeleportPlayerToPosition

        [ClientRpc]
        void TeleportPlayerToPositionClientRpc(string? playerName, Vector3 pos)
        {
            Logger.LogDebug("TeleportPlayerToPositionClientRpc");
            PlayerControllerB player = GetPlayerByNameOrFirstOne(playerName);

            if ((bool)(UnityEngine.Object)FindObjectOfType<AudioReverbPresets>())
                FindObjectOfType<AudioReverbPresets>()
                    .audioPresets[2]
                    .ChangeAudioReverbForPlayer(player);
            player.isInElevator = false;
            player.isInHangarShipRoom = false;
            player.isInsideFactory = true;
            player.averageVelocity = 0.0f;
            player.velocityLastFrame = Vector3.zero;
            player.TeleportPlayer(pos);
            player.beamOutParticle.Play();
            player.movementAudio.PlayOneShot(_teleporterBeamClip);
        }
        #endregion TeleportPlayerToPosition

        #region SwitchPlayerPosition

        [ServerRpc(RequireOwnership = false)]
        void SwitchPlayersPositionServerRpc(string? playerName)
        {
            SwitchPlayersPositionClientRpc(playerName);
        }

        [ClientRpc]
        void SwitchPlayersPositionClientRpc(string? playerName)
        {
            Logger.LogDebug("SwitchPlayerPositionClientRpc");

            List<PlayerControllerB> players = GetActivePlayers()
                .Where((player) => !player.isPlayerDead)
                .Where((player) => player.isPlayerControlled)
                .ToList();
            if (players.Count < 2)
            {
                return;
            }

            PlayerControllerB player = GetPlayerByNameOrFirstOne(playerName);
            PlayerControllerB player2;

            do
            {
                player2 = players[_rng.Next(players.Count)];
            } while (player2.NetworkObjectId == player.NetworkObjectId);

            Logger.LogDebug(
                "Switching positions of " + player.playerUsername + " and " + player2.playerUsername
            );

            if ((bool)(UnityEngine.Object)FindObjectOfType<AudioReverbPresets>())
            {
                FindObjectOfType<AudioReverbPresets>()
                    .audioPresets[2]
                    .ChangeAudioReverbForPlayer(player);
                FindObjectOfType<AudioReverbPresets>()
                    .audioPresets[2]
                    .ChangeAudioReverbForPlayer(player2);
            }

            Vector3 playerPos = new Vector3(
                player.transform.position.x,
                player.transform.position.y,
                player.transform.position.z
            );
            Vector3 player2Pos = new Vector3(
                player2.transform.position.x,
                player2.transform.position.y,
                player2.transform.position.z
            );

            player.isInElevator = false;
            player.isInHangarShipRoom = false;
            player.isInsideFactory = true;
            player.averageVelocity = 0.0f;
            player.velocityLastFrame = Vector3.zero;
            player.TeleportPlayer(player2Pos);
            player.beamOutParticle.Play();

            player2.isInElevator = false;
            player2.isInHangarShipRoom = false;
            player2.isInsideFactory = true;
            player2.averageVelocity = 0.0f;
            player2.velocityLastFrame = Vector3.zero;
            player2.TeleportPlayer(playerPos);
            player2.beamOutParticle.Play();

            ShipTeleporter shipTeleporter = FindObjectOfType<ShipTeleporter>();
            if (shipTeleporter)
            {
                player.movementAudio.PlayOneShot(shipTeleporter.teleporterBeamUpSFX);
                player2.movementAudio.PlayOneShot(shipTeleporter.teleporterBeamUpSFX);
            }
        }

        #endregion SwitchPlayerPosition
        
        #region MeteorShower
        [ServerRpc(RequireOwnership = false)]
        public void StartMeteorEventServerRpc()
        {
            Logger.LogDebug("StartMeteorEventServerRpc");
            
            TimeOfDay instance = TimeOfDay.Instance;
            instance.meteorShowerAtTime = -1f;
            instance.MeteorWeather.SetStartMeteorShower();
        }
        #endregion MeteorShower
        
        #region OpenAllSteamValveHazard
        
        [ServerRpc(RequireOwnership = false)]
        public void OpenAllSteamValveHazardServerRpc()
        {
            OpenAllSteamValveHazardClientRpc();
        }

        [ClientRpc]
        public void OpenAllSteamValveHazardClientRpc()
        {
            Logger.LogDebug("OpenAllSteamValveHazardClientRpc");

            List<SteamValveHazard> steamValves = FindObjectsOfType<SteamValveHazard>().ToList();
            Logger.LogDebug(steamValves.Count + " steamValve found");
            foreach (var steamValve in steamValves)
            {
                Logger.LogDebug("Opening steamValve");
                steamValve.BurstValve();
                steamValve.CrackValve();
                steamValve.valveHasBurst = true;
                steamValve.valveHasCracked = true;
                steamValve.valveHasBeenRepaired = false;
                steamValve.currentFogSize = 10f;
            }
        }
        #endregion OpenAllSteamValveHazard
        
        #region TurnOffLights
        
        [ServerRpc(RequireOwnership = false)]
        public void TurnOffLightsServerRpc()
        {
            BreakerBox breakerBox = FindObjectOfType<BreakerBox>();
            Logger.LogDebug("BreakerBox " + (breakerBox != null ? "found" : "not found"));

            if (breakerBox != null)
            {
                bool found = false;
                int switchIndex = -1;
                AnimatedObjectTrigger breakerBoxSwitch;

                do
                {
                    switchIndex++;
                    breakerBoxSwitch = breakerBox.breakerSwitches[switchIndex].gameObject
                        .GetComponent<AnimatedObjectTrigger>();
                    found |= breakerBoxSwitch.boolValue;
                } while (!breakerBoxSwitch.boolValue && switchIndex < breakerBox.breakerSwitches.Length - 1);

                if (found)
                {
                    TurnOffLightsClientRpc(switchIndex);   
                }
            }
        }

        [ClientRpc]
        public void TurnOffLightsClientRpc(int switchIndex)
        {
            Logger.LogDebug("TurnOffLightsClientRpc");

            BreakerBox breakerBox = FindObjectOfType<BreakerBox>();

            if (breakerBox != null)
            {
                AnimatedObjectTrigger breakerBoxSwitch = breakerBox.breakerSwitches[switchIndex].gameObject
                    .GetComponent<AnimatedObjectTrigger>();
                breakerBox.breakerSwitches[switchIndex].SetBool("turnedLeft", false);
                breakerBoxSwitch.boolValue = false;
                breakerBoxSwitch.setInitialState = false;
                
                breakerBox.SwitchBreaker(false);
            }
        }
        #endregion TurnOffLights
        
        #region LeaveEarly
        [ServerRpc(RequireOwnership = false)]
        public void LeaveEarlyServerRpc()
        {
            Logger.LogDebug("LeaveEarlyServerRpc");
            
            TimeOfDay instance = TimeOfDay.Instance;
            instance.votedShipToLeaveEarlyThisRound = true;
            instance.SetShipLeaveEarlyServerRpc();
        }
        #endregion LeaveEarly

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref _id);
        }

        private static PlayerControllerB GetPlayerByNameOrFirstOne(string? entityName)
        {
            List<PlayerControllerB> activePlayers = GetActivePlayers();
            return activePlayers.FirstOrDefault(x => x.name == entityName)
                ?? StartOfRound.Instance.allPlayerScripts[0];
        }

        private static List<PlayerControllerB> GetActivePlayers()
        {
            List<PlayerControllerB> activePlayers = [];
            activePlayers.AddRange(
                StartOfRound
                    .Instance.allPlayerScripts.Where(player =>
                        player.isActiveAndEnabled && player.playerSteamId > 0
                    )
                    .ToList()
            );
            return activePlayers;
        }

        static float NextFloat(Random random, float rangeMin, float rangeMax)
        {
            double range = (double)rangeMin - (double)rangeMax;
            double sample = random.NextDouble();
            double scaled = (sample * range) + rangeMin;
            return (float)scaled;
        }
        
        static Dictionary<T, int> ConfigEffectParsing<T>(string effectsRarityStr) where T : struct {
            Dictionary<T, int> effectsRarity = new Dictionary<T, int>();
		
            foreach (string entry in effectsRarityStr.Split(',').Select(s => s.Trim())) {
                string[] entryParts = entry.Split(':');

                if (entryParts.Length != 2) {
                    continue;
                }
                string effectName = entryParts[0];
                int spawnRate;

                if (!int.TryParse(entryParts[1], out spawnRate)) {
                    continue;
                }

                if (Enum.TryParse(effectName, true, out T effect)) {
                    effectsRarity[effect] = spawnRate;
                } else {
                    Logger.LogDebug($"Effect {effectName} was not recognized");
                }
            }
            return effectsRarity;
        }
    }
}
