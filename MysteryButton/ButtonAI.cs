using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UIElements;
using Logger = BepInEx.Logging.Logger;
using Random = System.Random;

namespace MysteryButton
{
    public class ButtonAI : EnemyAI, INetworkSerializable
    {
        private static int cpt = 0;

        private static bool IS_TEST = false;

        internal static ManualLogSource logger = Logger.CreateLogSource(
            "Elirasza.MysteryButton.ButtonAI"
        );

        private static readonly int PlayUsed = Animator.StringToHash("playUsed");
        
        private static readonly int PlayDestroyed = Animator.StringToHash("playDestroyed");
        
        private static readonly int PlayIdleBouncing = Animator.StringToHash("playIdleBouncing");

        private Random rng;

        private NetworkVariable<bool> isLock = new NetworkVariable<bool>();

        private bool isLocalLock;

        private int id;

        private AudioClip buttonAppearClip;

        private AudioClip buttonUsedClip;

        private AudioClip buttonUsedMalusClip;

        private List<AudioClip> playerMalusClips;

        private bool canExplodeLandmines;

        private bool canMakeTurretsBerserk;

        private EnemyVent nearestVent;

        private Animator animator;

        public override void Start()
        {
            logger.LogInfo("ButtonAI::Start");
            isLocalLock = false;

            animator = gameObject.GetComponentInChildren<Animator>();
            
            float waitTime = 10f;
            InvokeRepeating ("PlayIdleAnimation", 10f, waitTime);

            AudioSource audioSource = gameObject.GetComponent<AudioSource>();
            logger.LogInfo("AudioSource is " + (audioSource ? " not null" : "null"));
            creatureSFX = gameObject.GetComponent<AudioSource>();

            AudioClip[] audioClips = enemyType?.audioClips ?? [];
            buttonAppearClip = audioClips[0];
            buttonUsedClip = audioClips[1];
            buttonUsedMalusClip = audioClips[2];
            playerMalusClips = [audioClips[3], audioClips[4]];

            id = cpt++;
            enemyHP = 100;
            rng = new Random((int)NetworkObjectId);

            if (creatureSFX && enemyType?.overrideVentSFX)
            {
                // creatureSFX.PlayOneShot(enemyType?.overrideVentSFX);
                creatureSFX.PlayOneShot(buttonAppearClip);
            }

            List<Landmine> landmines = FindObjectsOfType<Landmine>()
                .Where(mine => !mine.hasExploded)
                .ToList();
            canExplodeLandmines = landmines.Count > 0;
            
            List<Turret> turrets = FindObjectsOfType<Turret>().ToList();
            canMakeTurretsBerserk = turrets.Count > 0;
            
            List<EnemyVent> vents = RoundManager.Instance.allEnemyVents.ToList();

            if (vents != null && vents.Count > 0)
            {
                nearestVent = vents[0];

                foreach (EnemyVent vent in vents)
                {
                    float distBetweenNearestVentAndButton =
                        Vector3.Distance(nearestVent.transform.position, transform.position);
                    float distBetweenVentAndButton = Vector3.Distance(vent.transform.position, transform.position);
                    if (distBetweenNearestVentAndButton > distBetweenVentAndButton)
                    {
                        nearestVent = vent;
                    }
                }
            }

            base.Start();
        }

        private void PlayIdleAnimation()
        {
            if (animator && !isLocalLock && !isLock.Value)
            {
                animator.SetTrigger(PlayIdleBouncing);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            logger.LogInfo("ButtonAI::OnNetworkSpawn, IsServer=" + IsServer);
            if (IsServer)
            {
                isLock.Value = false;
                NetworkManager.OnClientConnectedCallback += NetworkManager_OnClientConnectedCallback;
            }
            else
            {
                isLock.OnValueChanged += OnSomeValueChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsServer)
            {
                isLock.OnValueChanged -= OnSomeValueChanged;
            }
        }

        private void NetworkManager_OnClientConnectedCallback(ulong obj)
        {
            InitNetworkVariables();
        }
        
        private void OnSomeValueChanged(bool previous, bool current)
        {
            logger.LogInfo($"Detected NetworkVariable Change: Previous: {previous} | Current: {current}");
            isLock.Value = current;
        }

        private void InitNetworkVariables()
        {
            isLock.Value = false;
            NetworkManager.OnClientConnectedCallback -= NetworkManager_OnClientConnectedCallback;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetLockServerRpc()
        {
            logger.LogInfo("ButtonAI::SetLock");
            isLock.Value = true;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (!isLocalLock && !isLock.Value)
            {
                isLocalLock = true;
                logger.LogInfo("ButtonAI::OnCollideWithPlayer, ButtonAI::id=" + id);
                SetLockServerRpc();
                PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();

                if (player != null)
                {
                    logger.LogInfo(
                        "ButtonAI::OnCollideWithPlayer, player EXISTS for collider "
                        + other.gameObject.name
                    );
                    
                    DoEffect(player.name);
                }
            }
        }

        public void DoEffect(string playerName)
        {
            int effect = rng.Next(0, 100);
            logger.LogInfo("effect=" + effect);

            bool isBonus = effect < 50;

            if (isBonus)
            {
                logger.LogInfo("Bonus effect");
                DoBonusEffect(playerName);
            }
            else
            {
                logger.LogInfo("Malus effect");
                DoMalusEffect(playerName);
            }

            KillButtonServerRpc(isBonus);
        }

        private void DoBonusEffect(string playerName)
        {
            int effect = rng.Next(0, 100);
            logger.LogInfo("Bonus effect=" + effect);

            if (IS_TEST)
            {
                LeaveEarlyServerRpc();
            }
            else
            {
                if (effect < 30)
                {
                    SpawnScrapServerRpc(playerName);
                }
                else if (effect < 60)
                {
                    int amount = rng.Next(1, 6);
                    SpawnScrapServerRpc(playerName, amount);
                }
                else if (effect < 90)
                {
                    SpawnSpecificScrapServerRpc(playerName, 1);
                }
                else if (effect < 91)
                {
                    int amount = rng.Next(1, 11);
                    SpawnSpecificScrapServerRpc(playerName, amount);
                }
                else if (canExplodeLandmines)
                {
                    ExplodeLandminesServerRpc();
                }
                else
                {
                    RevivePlayerServerRpc(playerName);
                }
            }
        }

        private void DoMalusEffect(string playerName)
        {
            int effect = rng.Next(0, 100);
            logger.LogInfo("Malus effect=" + effect);

            if (IS_TEST)
            {
                LeaveEarlyServerRpc();
            }
            else
            {
                if (effect < 5)
                {
                    StartMeteorEventServerRpc();
                }
                else if (effect < 20)
                {
                    TeleportPlayerToRandomPositionServerRpc(playerName);
                }
                else if (effect < 40)
                {
                    SwitchPlayersPositionServerRpc(playerName);
                }
                else if (effect < 55)
                {
                    PlayerDrunkServerRpc();
                }
                else if (effect < 56)
                {
                    LeaveEarlyServerRpc();
                }
                else if (effect < 60)
                {
                    RandomPlayerIncreaseInsanityServerRpc();
                }
                else if (effect < 70 && canMakeTurretsBerserk)
                {
                    BerserkTurretServerRpc();
                }
                else if (effect < 80 && nearestVent != null)
                {
                    SpawnEnemyServerRpc(1);
                }
                else if (effect < 81 && nearestVent != null)
                {
                    SpawnEnemyServerRpc(rng.Next(1, 5));
                }
                else
                {
                    int open = rng.Next(0, 100);
                    if (open < 50)
                    {
                        OpenAllDoorsServerRpc(playerName);
                    }
                    else
                    {
                        CloseAllDoorsServerRpc(playerName);
                    }
                }
            }
        }

        #region KillButton
        [ServerRpc(RequireOwnership = false)]
        public void KillButtonServerRpc(bool isBonus)
        {
            KillButtonClientRpc(isBonus);
        }

        [ClientRpc]
        public void KillButtonClientRpc(bool isBonus)
        {
            logger.LogInfo("ButtonAI::KillButtonClientRpc");
            if (creatureSFX)
            {
                creatureSFX.Stop();
                if (isBonus)
                {
                    creatureSFX.PlayOneShot(buttonUsedClip);
                }
                else
                {
                    creatureSFX.PlayOneShot(buttonUsedMalusClip);
                }
            }

            if (isBonus) 
            {
                Material buttonUsedMaterial = MysteryButton.buttonUsedMaterial;
                transform.Find("MysteryButton/SpringBones/Bone.004/MysteryButton_Bouton").GetComponent<MeshRenderer>().material = buttonUsedMaterial;
            }

            if (animator)
            {
                if (isBonus)
                {
                    animator.SetBool(PlayUsed, true);
                }
                else
                {
                    animator.SetBool(PlayDestroyed, true);
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
            logger.LogInfo("ButtonAI::PlayerDrunkClientRpc");
            if (StartOfRound.Instance != null)
            {
                foreach (PlayerControllerB player in GetActivePlayers())
                {
                    logger.LogInfo("Client: Apply effect to " + player.playerUsername);
                    player.drunkness = 5f;
                }
            }
        }
        #endregion PlayerDrunk

        #region RevivePlayer
        [ServerRpc(RequireOwnership = false)]
        void RevivePlayerServerRpc(string? playerName)
        {
            logger.LogInfo("ButtonAI:RevivePlayerServerRpc");
            var player = GetPlayerByNameOrFirstOne(playerName);
            var deadPlayers = StartOfRound
                .Instance.allPlayerScripts.Where((p) => p.isPlayerDead)
                .ToList();
            if (deadPlayers.Count > 0)
            {
                var deadPlayer = deadPlayers[rng.Next(0, deadPlayers.Count)];
                RevivePlayerClientRpc(playerName, deadPlayer.name);
                TeleportPlayerToPositionClientRpc(deadPlayer.name, player.transform.position);
            }
        }

        [ClientRpc]
        void RevivePlayerClientRpc(string? playerName, string? deadPlayerName)
        {
            logger.LogInfo("ButtonAI:RevivePlayerClientRpc");
            StartOfRound instance = StartOfRound.Instance;
            var player = GetPlayerByNameOrFirstOne(playerName);
            var deadPlayer = GetPlayerByNameOrFirstOne(deadPlayerName);
            var deadPlayerIndex = instance.allPlayerScripts.IndexOf(p => p.name == deadPlayer.name);

            logger.LogInfo(
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
            logger.LogInfo("ButtonAI::RandomPlayerIncreaseInsanityClientRpc");
            if (StartOfRound.Instance != null)
            {
                PlayerControllerB[] currentPlayers = GetActivePlayers()
                    .Where(player => (!player.isPlayerDead || player.isPlayerControlled) && player.playerSteamId != 0)
                    .ToArray();
                PlayerControllerB player = currentPlayers[rng.Next(currentPlayers.Length)];
                player.insanityLevel = player.maxInsanityLevel;
                player.JumpToFearLevel(1.25F);
                player.movementAudio.PlayOneShot(
                    playerMalusClips[rng.Next(0, playerMalusClips.Count)]
                );
                player.JumpToFearLevel(1.25f);
                logger.LogInfo("Client: Apply max insanity to " + player.playerUsername);
            }
        }
        #endregion RandomPlayerIncreaseInsanity

        #region OpenAllDoors
        [ServerRpc(RequireOwnership = false)]
        void OpenAllDoorsServerRpc(string? name)
        {
            OpenAllDoorsClientRpc(name);
        }

        [ClientRpc]
        void OpenAllDoorsClientRpc(string? name)
        {
            logger.LogInfo("ButtonAI::OpenAllDoorsClientRpc");
            var player = GetPlayerByNameOrFirstOne(name);

            List<DoorLock> doors = FindObjectsOfType<DoorLock>().ToList();
            foreach (DoorLock door in doors)
            {
                bool openLockedDoor = !door.isLocked || rng.Next(0, 10) < 2;
                if (!door.isDoorOpened && openLockedDoor)
                {
                    if (door.isLocked)
                    {
                        logger.LogInfo("Unlocking door id=" + door.NetworkObjectId);
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
        void CloseAllDoorsServerRpc(string? name)
        {
            CloseAllDoorsClientRpc(name);
        }

        [ClientRpc]
        void CloseAllDoorsClientRpc(string? name)
        {
            logger.LogInfo("ButtonAI::CloseAllDoorsClientRpc");
            var player = GetPlayerByNameOrFirstOne(name);

            List<DoorLock> doors = FindObjectsOfType<DoorLock>().ToList();
            logger.LogInfo("CloseAllDoors: " + doors.Count);
            foreach (DoorLock door in doors)
            {
                if (door.isDoorOpened)
                {
                    door.OpenOrCloseDoor(player);

                    if (rng.Next(0, 10) < 2)
                    {
                        logger.LogInfo("Locking door id=" + door.NetworkObjectId);
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
            logger.LogInfo("ButtonAI::ExplodeLandminesServerRpc");
            List<Landmine> landmines = FindObjectsOfType<Landmine>()
                .Where(mine => !mine.hasExploded)
                .ToList();
            logger.LogInfo(landmines.Count + " landmines found");
            foreach (var landmine in landmines)
            {
                logger.LogInfo("Exploding landmine id=" + landmine.NetworkObjectId);
                landmine.ExplodeMineServerRpc();
            }
        }
        #endregion ExplodeLandmines

        #region SpawnScrap

        [ServerRpc(RequireOwnership = false)]
        void SpawnScrapServerRpc(string? entityName, int amount)
        {
            logger.LogInfo("ButtonAI::SpawnScrapServerRpc");
            SpawnScrap(entityName, null, amount);
        }

        [ServerRpc(RequireOwnership = false)]
        void SpawnScrapServerRpc(string? entityName)
        {
            logger.LogInfo("ButtonAI::SpawnScrapServerRpc");
            SpawnScrap(entityName, null, 1);
        }

        [ServerRpc(RequireOwnership = false)]
        void SpawnSpecificScrapServerRpc(string? entityName, int amount)
        {
            logger.LogInfo("ButtonAI::SpawnSpecificScrapServerRpc");
            List<Item> allScrapList = StartOfRound
                .Instance.allItemsList.itemsList.Where(
                    (item) => item.isScrap && item.maxValue > 150
                )
                .ToList();
            SpawnScrap(entityName, allScrapList[rng.Next(0, allScrapList.Count - 1)], amount);
        }

        void SpawnScrap(string? entityName, Item? specificScrap, int amount)
        {
            List<Item> allScrapList = StartOfRound
                .Instance.allItemsList.itemsList.Where((item) => item.isScrap)
                .ToList();
            int allItemListSize = allScrapList.Count;

            var player = GetPlayerByNameOrFirstOne(name);

            for (int i = 0; i < amount; i++)
            {
                int allItemListIndex = rng.Next(0, allItemListSize);
                Item randomItem = specificScrap ?? allScrapList[allItemListIndex];

                float angle = NextFloat(rng, 0, 2f * Mathf.PI);
                Vector3 position =
                    player.transform.position
                    + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * NextFloat(rng, 1f, 1.5f);

                GameObject obj = Instantiate(randomItem.spawnPrefab, position, Quaternion.identity);

                int value = rng.Next(randomItem.minValue, randomItem.maxValue);
                float weight = randomItem.weight;

                logger.LogInfo(
                    "Spawning item=" + randomItem.name + ", value=" + value + ", weight=" + weight
                );

                ScanNodeProperties scan = obj.GetComponent<ScanNodeProperties>();
                if (scan == null)
                {
                    logger.LogInfo(
                        "No scan found, creating with scrapValue="
                            + value
                            + " and subText="
                            + $"\"Value: ${value}\""
                    );
                    scan = obj.AddComponent<ScanNodeProperties>();
                    scan.scrapValue = value;
                    scan.subText = $"Value: ${value.ToString()}";
                    scan.headerText = randomItem.name;
                }

                obj.GetComponent<GrabbableObject>().fallTime = 0f;
                obj.GetComponent<GrabbableObject>().scrapValue = value;
                obj.GetComponent<GrabbableObject>().itemProperties.weight = weight;
                obj.GetComponent<GrabbableObject>().itemProperties.creditsWorth = value;
                obj.GetComponent<GrabbableObject>().SetScrapValue(value);
                obj.GetComponent<NetworkObject>().Spawn();
            }
        }

        #endregion SpawnScrap

        #region SpawnEnemy

        [ServerRpc(RequireOwnership = false)]
        void SpawnEnemyServerRpc(int amount)
        {
            logger.LogInfo("ButtonAI::SpawnEnemyServerRpc, amount=" + amount);
            List<SpawnableEnemyWithRarity> enemies = StartOfRound.Instance.currentLevel.Enemies;
            int allEnemiesListSize = enemies.Count;
            
            for (int i = 0; i < amount; i++)
            {
                int allEnemiesListIndex = rng.Next(0, allEnemiesListSize);
                SpawnableEnemyWithRarity randomEnemy = enemies[allEnemiesListIndex];

                logger.LogInfo("Spawning enemy=" + randomEnemy.enemyType.name);
                
                GameObject obj = Instantiate(
                    randomEnemy.enemyType.enemyPrefab,
                    nearestVent.transform.position,
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
            logger.LogInfo("ButtonAI::BerserkTurretClientRpc");
            
            List<Turret> turrets = FindObjectsOfType<Turret>().ToList();
            
            logger.LogInfo(turrets.Count + " turrets found");

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
            logger.LogInfo("ButtonAI::TeleportPlayerToRandomPositionClientRpc");
            PlayerControllerB player = GetPlayerByNameOrFirstOne(playerName);

            int randomIndex = rng.Next(0, RoundManager.Instance.insideAINodes.Length);
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

            ShipTeleporter shipTeleporter = FindObjectOfType<ShipTeleporter>();
            if (shipTeleporter)
            {
                player.movementAudio.PlayOneShot(shipTeleporter.teleporterBeamUpSFX);
            }
        }

        #endregion TeleportPlayerToRandomPosition

        #region TeleportPlayerToPosition

        [ClientRpc]
        void TeleportPlayerToPositionClientRpc(string? playerName, Vector3 pos)
        {
            logger.LogInfo("ButtonAI::TeleportPlayerToPositionClientRpc");
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

            ShipTeleporter shipTeleporter = FindObjectOfType<ShipTeleporter>();
            if (shipTeleporter)
            {
                player.movementAudio.PlayOneShot(shipTeleporter.teleporterBeamUpSFX);
            }
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
            logger.LogInfo("ButtonAI::SwitchPlayerPositionClientRpc");

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
                player2 = players[rng.Next(players.Count)];
            } while (player2.NetworkObjectId == player.NetworkObjectId);

            logger.LogInfo(
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
            logger.LogInfo("ButtonAI::StartMeteorEventServerRpc");
            
            TimeOfDay instance = TimeOfDay.Instance;
            instance.meteorShowerAtTime = -1f;
            instance.MeteorWeather.SetStartMeteorShower();
        }
        #endregion MeteorShower
        
        #region LeaveEarly
        [ServerRpc(RequireOwnership = false)]
        public void LeaveEarlyServerRpc()
        {
            logger.LogInfo("ButtonAI::LeaveEarlyServerRpc");
            
            TimeOfDay instance = TimeOfDay.Instance;
            instance.votedShipToLeaveEarlyThisRound = true;
            instance.SetShipLeaveEarlyServerRpc();
        }
        #endregion LeaveEarly

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
        }

        private static PlayerControllerB GetPlayerByNameOrFirstOne(string? name)
        {
            List<PlayerControllerB> activePlayers = GetActivePlayers();
            return activePlayers.FirstOrDefault(x => x.name == name)
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
    }
}
