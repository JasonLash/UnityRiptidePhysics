using System.Collections.Generic;
using UnityEngine;
using Riptide;

public class ClientInput
{
    public bool[] Inputs = new bool[5];
    public ushort currentTick = 0;
}

public class Player : MonoBehaviour
{
    public static Dictionary<ushort, Player> List { get; private set; } = new Dictionary<ushort, Player>();

    public ushort Id { get; private set; }
    public string Username { get; private set; }

    [SerializeField] public Rigidbody rb;

    private ClientInput lastReceivedInputs = new ClientInput();

    public float playerMovementImpulse = 0.5f;

    public Vector3 playerVelocity;
    public Vector3 playerAngularVelocity;


    private void Start()
    {
        Physics.simulationMode = SimulationMode.Script;

        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
    }

    private void HandleClientInput(ClientInput[] inputs, ushort clientID)
    {
        if (inputs.Length == 0) return;
      
        if (inputs[inputs.Length - 1].currentTick >= lastReceivedInputs.currentTick)
        {
            int start = lastReceivedInputs.currentTick > inputs[0].currentTick ? (lastReceivedInputs.currentTick - inputs[0].currentTick) : 0;

            //Disable all other players so the physics only steps for the player with the input
            //Save the players velocity
            foreach (KeyValuePair<ushort, Player> entry in List)
			{
				ushort playerId = entry.Key;
				Player player = entry.Value;
				if (playerId != clientID)
				{
                    player.playerVelocity = player.rb.velocity;
                    player.playerAngularVelocity = player.rb.angularVelocity;
                    player.rb.isKinematic = true;
                }	
				else
					player.rb.isKinematic = false;
			}

            //Simulate the users inputs
            for (int i = start; i < inputs.Length - 1; i++)
            {
                PhysicsStep(rb, inputs[i].Inputs);
                Physics.Simulate(NetworkManager.Singleton.TickRate);
                SendMovement((ushort)(inputs[i].currentTick + 1));
            }

            //Re-enable all players with their saved velocities 
            foreach (KeyValuePair<ushort, Player> entry in List)
			{
				Player player = entry.Value;
                ushort playerId = entry.Key;
                player.rb.isKinematic = false;
                if (playerId != clientID)
                {
                    player.rb.velocity = player.playerVelocity;
                    player.rb.angularVelocity = player.playerAngularVelocity;
                }
			}

			lastReceivedInputs = inputs[inputs.Length - 1];
        }
    }

    private void PhysicsStep(Rigidbody rigidbody, bool[] inputs)
    {
        if (inputs[0])
        {
            rigidbody.AddForce(Vector3.forward * playerMovementImpulse, ForceMode.Impulse);
        }
        if (inputs[1])
        {
            rigidbody.AddForce(-Vector3.forward * playerMovementImpulse, ForceMode.Impulse);
        }
        if (inputs[2])
        {
            rigidbody.AddForce(-Vector3.right * playerMovementImpulse, ForceMode.Impulse);
        }
        if (inputs[3])
        {
            rigidbody.AddForce(Vector3.right * playerMovementImpulse, ForceMode.Impulse);
        }
        if (inputs[4])
        {
            rigidbody.AddForce(Vector3.up * playerMovementImpulse, ForceMode.Impulse);
        } 
    }

    private void OnDestroy()
    {
        List.Remove(Id);
    }

    public static void Spawn(ushort id, string username)
    {
        Player player = Instantiate(NetworkManager.Singleton.PlayerPrefab, new Vector3(0f, 1f, 0f), Quaternion.identity).GetComponent<Player>();
        player.name = $"Player {id} ({(username == "" ? "Guest" : username)})";
        player.Id = id;
        player.Username = username;

        player.SendSpawn();
        List.Add(player.Id, player);
    }

    #region Messages
    /// <summary>Sends a player's info to the given client.</summary>
    /// <param name="toClient">The client to send the message to.</param>
    public void SendSpawn(ushort toClient)
    {
        NetworkManager.Singleton.Server.Send(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)), toClient);
    }
    /// <summary>Sends a player's info to all clients.</summary>
    private void SendSpawn()
    {
        NetworkManager.Singleton.Server.SendToAll(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)));
    }

    private Message GetSpawnData(Message message)
    {
        message.AddUShort(Id);
        message.AddString(Username);
        message.AddVector3(transform.position);
        return message;
    }

    private void SendMovement(ushort clientTick)
    {
        Message message = Message.Create(MessageSendMode.Unreliable, ServerToClientId.PlayerMovement);
        message.AddUShort(Id);
        message.AddUShort(clientTick);
        message.AddVector3(transform.position);
        message.AddVector3(transform.forward);
        message.AddVector3(rb.velocity);
        message.AddVector3(rb.angularVelocity);
        message.AddQuaternion(transform.rotation);
        NetworkManager.Singleton.Server.SendToAll(message);
    }

    [MessageHandler((ushort)ClientToServerId.PlayerName)]
    private static void PlayerName(ushort fromClientId, Message message)
    {
        Spawn(fromClientId, message.GetString());
    }

    [MessageHandler((ushort)ClientToServerId.PlayerInput)]
    private static void PlayerInput(ushort fromClientId, Message message)
    {
        Player player = List[fromClientId];


        byte inputsQuantity = message.GetByte();
        ClientInput[] inputs = new ClientInput[inputsQuantity];

        // Now we loops to get all the inputs sent by the client and store them in an array 
        for (int i = 0; i < inputsQuantity; i++)
        {
            inputs[i] = new ClientInput
            {
                Inputs = message.GetBools(5),
                currentTick = message.GetUShort()
            };
        }

        player.HandleClientInput(inputs, player.Id);
    }
    #endregion
}

