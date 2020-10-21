// -----------------------------------------------------------------------
// <copyright file="GhostMode.cs" company="Exiled Team">
// Copyright (c) Exiled Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.Events.Patches.Generic
{
#pragma warning disable SA1313
    using System;
    using System.Collections.Generic;

    using CustomPlayerEffects;

    using Exiled.API.Features;

    using HarmonyLib;

    using Mirror;

    using UnityEngine;

    using Scp096 = PlayableScps.Scp096;

#pragma warning disable SA1028 // Code should not contain trailing whitespace
#pragma warning disable CS0618 // Type or member is obsolete (Player.TargetGhosts)
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1513 // Closing brace should be followed by blank line
#pragma warning disable SA1512 // Single-line comments should not be followed by blank line

    /// <summary>
    /// Patches <see cref="PlayerPositionManager.TransmitData"/>.
    /// </summary>
    [HarmonyPatch(typeof(PlayerPositionManager), nameof(PlayerPositionManager.TransmitData))]
    internal static class GhostMode
    {
        // The game uses this position as the ghost position,
        // update it if the game has updated it,
        // replace all 'Vector3.up * 6000f' with this
        private static readonly Vector3 GhostPos = Vector3.up * 6000f;

        // Keep in mind, changes affecting this code
        // have a high breakage rate, so be careful when
        // updating or adding new things
        private static bool Prefix(PlayerPositionManager __instance)
        {
            try
            {
                if (++__instance._frame != __instance._syncFrequency)
                    return false;

                __instance._frame = 0;

                List<GameObject> players = PlayerManager.players;
                __instance._usedData = players.Count;

                if (__instance._receivedData == null
                    || __instance._receivedData.Length < __instance._usedData)
                {
                    __instance._receivedData = new PlayerPositionData[__instance._usedData * 2];
                }

                for (int index = 0; index < __instance._usedData; ++index)
                    __instance._receivedData[index] = new PlayerPositionData(ReferenceHub.GetHub(players[index]));

                if (__instance._transmitBuffer == null
                    || __instance._transmitBuffer.Length < __instance._usedData)
                {
                    __instance._transmitBuffer = new PlayerPositionData[__instance._usedData * 2];
                }

                foreach (GameObject gameObject in players)
                {
                    Player player = Player.Get(gameObject);
                    Array.Copy(__instance._receivedData, __instance._transmitBuffer, __instance._usedData);

                    if (player.Role.Is939())
                    {
                        for (int index = 0; index < __instance._usedData; ++index)
                        {
                            if (__instance._transmitBuffer[index].position.y < 800f)
                            {
                                ReferenceHub hub2 = ReferenceHub.GetHub(players[index]);

                                if (hub2.characterClassManager.CurRole.team != Team.SCP
                                    && hub2.characterClassManager.CurRole.team != Team.RIP
                                    && !players[index]
                                        .GetComponent<Scp939_VisionController>()
                                        .CanSee(player.ReferenceHub.characterClassManager.Scp939))
                                {
                                    MakeGhost(index, __instance._transmitBuffer);
                                }
                            }
                        }
                    }
                    else if (player.Role != RoleType.Spectator && player.Role != RoleType.Scp079)
                    {
                        for (int index = 0; index < __instance._usedData; ++index)
                        {
                            PlayerPositionData ppd = __instance._transmitBuffer[index];
                            Player currentTarget = Player.Get(players[index]);
                            Scp096 scp096 = player.ReferenceHub.scpsController.CurrentScp as Scp096;

                            if (currentTarget?.ReferenceHub == null)
                                continue;

                            Vector3 vector3 = ppd.position - player.ReferenceHub.playerMovementSync.RealModelPosition;
                            if (Math.Abs(vector3.y) > 35f)
                            {
                                MakeGhost(index, __instance._transmitBuffer);
                            }
                            else
                            {
                                float sqrMagnitude = vector3.sqrMagnitude;
                                if (player.ReferenceHub.playerMovementSync.RealModelPosition.y < 800f)
                                {
                                    if (sqrMagnitude >= 1764f)
                                    {
                                        MakeGhost(index, __instance._transmitBuffer);
                                    }
                                }
                                else if (sqrMagnitude >= 7225f)
                                {
                                    MakeGhost(index, __instance._transmitBuffer);
                                }
                                else
                                {
                                    if (scp096 != null
                                        && scp096.Enraged
                                        && !scp096.HasTarget(currentTarget.ReferenceHub)
                                        && currentTarget.Team != Team.SCP)
                                    {
#if DEBUG
                                        Log.Debug($"[Scp096@GhostModePatch] {player.UserId} can't see {currentTarget.UserId}");
#endif
                                        MakeGhost(index, __instance._transmitBuffer);
                                    }
                                    else if (currentTarget.ReferenceHub.playerEffectsController.GetEffect<Scp268>().Enabled)
                                    {
                                        bool flag = false;
                                        if (scp096 != null)
                                            flag = scp096.HasTarget(currentTarget.ReferenceHub);

                                        if (player.Role != RoleType.Scp079
                                            && player.Role != RoleType.Spectator
                                            && !flag)
                                        {
                                            MakeGhost(index, __instance._transmitBuffer);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // We do another FOR for the ghost things
                    // because it's hard to do it without
                    // whole code changes in the game code
                    for (var z = 0; z < __instance._usedData; z++)
                    {
                        var ppd = __instance._transmitBuffer[z];

                        // If it's already has the ghost position
                        if (ppd.position == GhostPos)
                            continue;

                        var target = Player.Get(ppd.playerID);
                        // If for some reason the player/their ref hub is null
                        if (target?.ReferenceHub == null)
                            continue;

                        if (player.IsInvisible || !PlayerCanSee(player, target.Id))
                        {
                            MakeGhost(z, __instance._transmitBuffer);
                        }
                        // Rotate the player because
                        // those movement checks are
                        // in client-side
                        else if (player.Role == RoleType.Scp173
                            && ((!Exiled.Events.Events.Instance.Config.CanTutorialBlockScp173
                                    && target.Role == RoleType.Tutorial)
                                || Scp173.TurnedPlayers.Contains(target)))
                        {
                            RotatePlayer(z, __instance._transmitBuffer, FindLookRotation(player.Position, target.Position));
                        }
                    }

                    NetworkConnection networkConnection = player.ReferenceHub.characterClassManager.netIdentity.isLocalPlayer
                        ? NetworkServer.localConnection
                        : player.ReferenceHub.characterClassManager.netIdentity.connectionToClient;
                    if (__instance._usedData <= 20)
                    {
                        networkConnection.Send(
                            new PlayerPositionManager.PositionMessage(__instance._transmitBuffer, (byte)__instance._usedData, 0), 1);
                    }
                    else
                    {
                        byte part;
                        for (part = 0; part < __instance._usedData / 20; ++part)
                            networkConnection.Send(new PlayerPositionManager.PositionMessage(__instance._transmitBuffer, 20, part), 1);
                        byte count = (byte)(__instance._usedData % (part * 20));
                        if (count > 0)
                            networkConnection.Send(new PlayerPositionManager.PositionMessage(__instance._transmitBuffer, count, part), 1);
                    }
                }

                return false;
            }
            catch (Exception exception)
            {
                Log.Error($"GhostMode error: {exception}");
                return true;
            }
        }

        private static Vector3 FindLookRotation(Vector3 player, Vector3 target) => (target - player).normalized;

        private static bool PlayerCanSee(Player source, int playerId) => source.TargetGhostsHashSet.Contains(playerId) || source.TargetGhosts.Contains(playerId);

        private static void MakeGhost(int index, PlayerPositionData[] buff) => buff[index] = new PlayerPositionData(GhostPos, buff[index].rotation, buff[index].playerID);

        private static void RotatePlayer(int index, PlayerPositionData[] buff, Vector3 rotation) => buff[index]
            = new PlayerPositionData(buff[index].position, Quaternion.LookRotation(rotation).eulerAngles.y, buff[index].playerID);
    }
}
