using System.Collections.Generic;
using UnityEngine;
using Riptide;

public class Player : MonoBehaviour
{
    public static Dictionary<ushort, Player> list = new Dictionary<ushort, Player>();

    [SerializeField] private ushort id;
    [SerializeField] private string username;

    public ServerSimulationState serverSimulationState = new ServerSimulationState();

    public void Move(Vector3 newPosition, Vector3 forward)
    {
        if (id != NetworkManager.Singleton.Client.Id)
		{
            transform.position = newPosition;
            transform.forward = forward;
		}
    }

    private void OnDestroy()
    {
        list.Remove(id);
    }

    public static void Spawn(ushort id, string username, Vector3 position)
    {
        Player player;
        if (id == NetworkManager.Singleton.Client.Id)
            player = Instantiate(NetworkManager.Singleton.LocalPlayerPrefab, position, Quaternion.identity).GetComponent<Player>();
        else
            player = Instantiate(NetworkManager.Singleton.PlayerPrefab, position, Quaternion.identity).GetComponent<Player>();

        player.name = $"Player {id} ({username})";
        player.id = id;
        player.username = username;
        list.Add(player.id, player);
    }

    #region Messages
    [MessageHandler((ushort)ServerToClientId.SpawnPlayer)]
    private static void SpawnPlayer(Message message)
    {
        Spawn(message.GetUShort(), message.GetString(), message.GetVector3());
    }

    [MessageHandler((ushort)ServerToClientId.PlayerMovement)]
    private static void PlayerMovement(Message message)
    {
        ushort playerId = message.GetUShort();
        ushort serverTick = message.GetUShort();
        Vector3 serverPlayerPosition = message.GetVector3();
        Vector3 serverPlayerLookDirection = message.GetVector3();
        Vector3 serverPlayerVelocity = message.GetVector3();
        Vector3 serverPlayerAngularVelocity = message.GetVector3();
        Quaternion serverPlayerRotation = message.GetQuaternion();

        if (list.TryGetValue(playerId, out Player player))
        {
            if (serverTick > player.serverSimulationState.currentTick)
            {
                player.serverSimulationState.position = serverPlayerPosition;
                player.serverSimulationState.currentTick = serverTick;
                player.serverSimulationState.velocity = serverPlayerVelocity;
                player.serverSimulationState.angularVelocity = serverPlayerAngularVelocity;
                player.serverSimulationState.rotation = serverPlayerRotation;
            }
            player.Move(serverPlayerPosition, serverPlayerLookDirection);


        }
    }
    #endregion
}

