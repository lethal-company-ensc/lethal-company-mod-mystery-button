﻿using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using LethalLib.Modules;
using Steamworks.Data;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Logger = BepInEx.Logging.Logger;
using Random = System.Random;

namespace MysteryButton
{
    public class ButtonAI : EnemyAI, INetworkSerializable
    {
        private static int cpt = 0;

        private static bool IS_TEST = true;

        internal static ManualLogSource logger = Logger.CreateLogSource("Elirasza.MysteryButton.ButtonAI");
        
        private static readonly int PlayDeath = Animator.StringToHash("playDeath");

        private Random rng;

        private bool isLock;

        private int id;

        public override void Start()
        {
            logger.LogInfo("ButtonAI::Start");

            AudioSource audioSource = gameObject.GetComponent<AudioSource>();
            logger.LogInfo("AudioSource is " + (audioSource ? " not null" : "null"));
            creatureSFX = gameObject.GetComponent<AudioSource>();

            id = cpt++;
            enemyHP = 100;
            rng = new Random((int)NetworkObjectId);
            isLock = false;

            if (creatureSFX && enemyType?.overrideVentSFX)
            {
                creatureSFX.PlayOneShot(enemyType?.overrideVentSFX);
            }

            base.Start();
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (!isLock)
            {
                logger.LogInfo("ButtonAI::OnCollideWithEnemy, ButtonAI::id=" + id);
                isLock = true;
                DoMalusEffect(collidedEnemy);
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (!isLock)
            {
                logger.LogInfo("ButtonAI::OnCollideWithPlayer, ButtonAI::id=" + id);
                isLock = true;
                PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();

                if (player != null)
                {
                    logger.LogInfo("ButtonAI::OnCollideWithPlayer, player EXISTS for collider " +
                                   other.gameObject.name);

                    int effect = rng.Next(0, 100);
                    logger.LogInfo("effect=" + effect);

                    if (effect < 50)
                    {
                        logger.LogInfo("Bonus effect");
                        DoBonusEffect(player);
                    }
                    else
                    {
                        logger.LogInfo("Malus effect");
                        DoMalusEffect(player);
                    }

                    KillEnemyServerRpc();
                }
            }
        }

        private void DoBonusEffect(PlayerControllerB player)
        {
            int effect = rng.Next(0, 100);
            logger.LogInfo("Bonus effect=" + effect);

            if (IS_TEST)
            {
                TeleportPlayerToRandomPositionServerRpc();
            }
            else
            {
                if (effect < 40)
                {
                    SpawnScrapServerRpc();
                }
                else if (effect < 50)
                {
                    SpawnSpecificScrapServerRpc(1);
                }
                else if (effect < 51)
                {
                    int amount = rng.Next(1, 11);
                    SpawnSpecificScrapServerRpc(amount);
                }
                else if (effect < 60)
                {
                    ChargeAllBatteriesServerRpc();
                }
            }
        }

        private void DoMalusEffect(NetworkBehaviour entity)
        {
            int effect = rng.Next(0, 100);
            logger.LogInfo("Malus effect=" + effect);

            if (IS_TEST)
            {
                TeleportPlayerToRandomPositionServerRpc();
            }
            else
            {
                if (effect < 45)
                {
                    PlayerDrunkServerRpc();
                }
                else if (effect < 50)
                {
                    RandomPlayerIncreaseInsanityServerRpc();
                }
                else if (effect < 70)
                {
                    DischargeAllBatteriesServerRpc();
                }
                else if (effect < 80)
                {
                    SpawnEnemyServerRpc(1);
                }
                else if (effect < 81)
                {
                    SpawnEnemyServerRpc(rng.Next(1, 5));
                }
                else
                {
                    string? entityName = entity?.name;
                    int open = rng.Next(0, 100);
                    if (open < 50)
                    {
                        OpenAllDoorsServerRpc(entityName);
                    }
                    else
                    {
                        CloseAllDoorsServerRpc(entityName);
                    }
                }
            }
        }

        #region KillEnemy
        [ServerRpc(RequireOwnership = false)]
        public void KillEnemyServerRpc()
        {
            KillEnemyClientRpc();
        }

        [ClientRpc]
        public void KillEnemyClientRpc()
        {
            logger.LogInfo("ButtonAI::KillEnemyClientRpc");
            if (creatureSFX && enemyType?.deathSFX)
            {
                creatureSFX.PlayOneShot(enemyType?.deathSFX);
            }

            Animator animator = gameObject.GetComponentInChildren<Animator>();
            if (animator)
            {
                animator.SetBool(PlayDeath, true);
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
                PlayerControllerB[] currentPlayers = GetActivePlayers().Where(player => player.playerSteamId != 0).ToArray();
                PlayerControllerB player = currentPlayers[rng.Next(currentPlayers.Length)];
                player.insanityLevel = player.maxInsanityLevel;
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
        
        #region ChargeAllBatteries

        [ServerRpc(RequireOwnership = false)]
        void ChargeAllBatteriesServerRpc()
        {
            ChargeAllBatteriesClientRpc();
        }

        [ClientRpc]
        void ChargeAllBatteriesClientRpc()
        {
            logger.LogInfo("ButtonAI::ChargeAllBatteriesClientRpc");
            List<GrabbableObject> batteryObjects = FindObjectsOfType<GrabbableObject>().ToList();
            foreach (GrabbableObject batteryObject in batteryObjects)
            {
                logger.LogInfo("Item=" + batteryObject.name + ", chargeBefore=" + batteryObject.insertedBattery.charge + ", isEmptyBefore=" + batteryObject.insertedBattery.empty);
                batteryObject.insertedBattery.charge = 1f;
                batteryObject.insertedBattery.empty = false;
            }
        }
        
        #endregion ChargeAllBatteries
        
        #region DischargeAllBatteries

        [ServerRpc(RequireOwnership = false)]
        void DischargeAllBatteriesServerRpc()
        {
            DischargeAllBatteriesClientRpc();
        }

        [ClientRpc]
        void DischargeAllBatteriesClientRpc()
        {
            logger.LogInfo("ButtonAI::DischargeAllBatteriesClientRpc");
            List<GrabbableObject> batteryObjects = FindObjectsOfType<GrabbableObject>().ToList();
            foreach (GrabbableObject batteryObject in batteryObjects)
            {
                logger.LogInfo("Item=" + batteryObject.name + ", chargeBefore=" + batteryObject.insertedBattery.charge + ", isEmptyBefore=" + batteryObject.insertedBattery.empty);
                batteryObject.insertedBattery.charge = 0f;
                batteryObject.insertedBattery.empty = true;
            }
        }
        
        #endregion DischargeAllBatteries

        #region SpawnScrap

        [ServerRpc(RequireOwnership = false)]
        void SpawnScrapServerRpc()
        {
            logger.LogInfo("ButtonAI::SpawnScrapServerRpc");
            SpawnScrap(null, 1);
        }

        [ServerRpc(RequireOwnership = false)]
        void SpawnSpecificScrapServerRpc(int amount)
        {
            logger.LogInfo("ButtonAI::SpawnSpecificScrapServerRpc");
            List<Item> allScrapList = StartOfRound.Instance.allItemsList.itemsList.Where((item) => item.isScrap && item.maxValue > 150).ToList();
            SpawnScrap(allScrapList[rng.Next(0, allScrapList.Count - 1)], amount);
        }

        void SpawnScrap(Item? specificScrap, int amount)
        {
            List<Item> allScrapList = StartOfRound.Instance.allItemsList.itemsList.Where((item) => item.isScrap).ToList();
            int allItemListSize = allScrapList.Count;

            for (int i = 0; i < amount; i++)
            {
                int randomIndex = rng.Next(0, RoundManager.Instance.insideAINodes.Length);
                int allItemListIndex = rng.Next(0, allItemListSize);
                Item randomItem = specificScrap ?? allScrapList[allItemListIndex];

                GameObject obj = Instantiate(randomItem.spawnPrefab,
                    RoundManager.Instance.insideAINodes[randomIndex].transform.position, Quaternion.identity);

                int value = rng.Next(randomItem.minValue, randomItem.maxValue);
                float weight = randomItem.weight;

                logger.LogInfo("Spawning item=" + randomItem.name + ", value=" + value + ", weight=" + weight);

                ScanNodeProperties scan = obj.GetComponent<ScanNodeProperties>();
                if (scan == null)
                {
                    logger.LogInfo("No scan found, creating with scrapValue=" + value + " and subText=" +
                                   $"\"Value: ${value}\"");
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
                int randomIndex = rng.Next(0, RoundManager.Instance.allEnemyVents.Length);
                SpawnableEnemyWithRarity randomEnemy = enemies[allEnemiesListIndex];

                logger.LogInfo("Spawning enemy=" + randomEnemy.enemyType.name);

                GameObject obj = Instantiate(randomEnemy.enemyType.enemyPrefab,
                    RoundManager.Instance.allEnemyVents[randomIndex].transform.position, Quaternion.identity);

                obj.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            }
        }

        #endregion SpawnEnemy
        
        #region TeleportPlayerToRandomPosition

        [ServerRpc(RequireOwnership = false)]
        void TeleportPlayerToRandomPositionServerRpc()
        {
            TeleportPlayerToRandomPositionClientRpc();
        }
        
        [ClientRpc]
        void TeleportPlayerToRandomPositionClientRpc()
        {
            logger.LogInfo("ButtonAI::TeleportPlayerToRandomPositionClientRpc");
            List<PlayerControllerB> players = GetActivePlayers();
            PlayerControllerB player = players[rng.Next(players.Count)];
            // PlayerControllerB player = GetPlayerByNameOrFirstOne(null);
            
            int randomIndex = rng.Next(0, RoundManager.Instance.insideAINodes.Length);
            var teleportPos = RoundManager.Instance.insideAINodes[randomIndex].transform.position;

            if ((bool) (UnityEngine.Object) FindObjectOfType<AudioReverbPresets>())
                FindObjectOfType<AudioReverbPresets>().audioPresets[2].ChangeAudioReverbForPlayer(player);
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
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
        }

        private static PlayerControllerB GetPlayerByNameOrFirstOne(string? name)
        {
            List<PlayerControllerB> activePlayers = GetActivePlayers();
            return activePlayers.FirstOrDefault(x => x.name == name) ?? StartOfRound.Instance.allPlayerScripts[0];
        }

        private static List<PlayerControllerB> GetActivePlayers()
        {
            List<PlayerControllerB> activePlayers = [];
            activePlayers.AddRange(StartOfRound.Instance.allPlayerScripts.Where(player => player.isActiveAndEnabled).ToList());
            return activePlayers;
        }
        
        static float NextFloat(Random random, float rangeMin, float rangeMax)
        {
            double range = (double) rangeMin - (double) rangeMax;
            double sample = random.NextDouble();
            double scaled = (sample * range) + rangeMin;
            return (float) scaled;
        }
    }
}