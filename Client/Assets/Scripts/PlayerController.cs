using UnityEngine;
using Riptide;

public class ClientInput
{
    public bool[] Inputs = new bool[5];
    public ushort currentTick = 0;
}

public class SimulationState
{
    public Vector3 position;
    public Quaternion rotation;
    public ushort currentTick = 0;
}

public class ServerSimulationState
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 angularVelocity;
    public ushort currentTick = 0;
}

public class PlayerController : MonoBehaviour
{
    private float deltaTickTime;
    public ushort cTick;
    public const int CacheSize = 1024;

    private ClientInput[] inputCache;
    private SimulationState[] clientStateCache;

    private Rigidbody rb;

    public float playerMovementImpulse;
    public float playerJumpYThreshold;

    public Player playerScript;

    private int lastCorrectedFrame;

    private Vector3 clientPosError;
    private Quaternion clientRotError;

    private void Awake()
    {
        playerScript = GetComponent<Player>();
    }

    private void Start()
    {
        Physics.simulationMode = SimulationMode.Script;

        deltaTickTime = Time.fixedDeltaTime;

        rb = GetComponent<Rigidbody>();
            
        rb.isKinematic = false;

            
        inputCache = new ClientInput[CacheSize];
        clientStateCache = new SimulationState[CacheSize];

        clientPosError = Vector3.zero;
        clientRotError = Quaternion.identity;

        cTick = 0;
    }

	private void FixedUpdate()
	{
        int cacheIndex = cTick % CacheSize;

        inputCache[cacheIndex] = GetInput();
        clientStateCache[cacheIndex] = CurrentSimulationState(rb);

        PhysicsStep(rb, inputCache[cacheIndex].Inputs);
        Physics.Simulate(deltaTickTime);
        SendInput();

        ++cTick;

        if (playerScript.serverSimulationState != null) Reconciliate();
    }

	private void Reconciliate()
    {
        if (playerScript.serverSimulationState.currentTick <= lastCorrectedFrame) return;
 
        ServerSimulationState serverSimulationState = playerScript.serverSimulationState;

        uint cacheIndex = (uint)serverSimulationState.currentTick % CacheSize;
        SimulationState cachedSimulationState = clientStateCache[cacheIndex];

        Vector3 positionError = serverSimulationState.position - cachedSimulationState.position;
        float rotationError = 1.0f - Quaternion.Dot(serverSimulationState.rotation, cachedSimulationState.rotation);

        if (positionError.sqrMagnitude > 0.0000001f || rotationError > 0.00001f)
        {
            Debug.Log("Correcting for error at tick " + serverSimulationState.currentTick + " (rewinding " + (cTick - cachedSimulationState.currentTick) + " ticks)");
            // capture the current predicted pos for smoothing
            Vector3 prevPos = rb.position + clientPosError;
            Quaternion prevRot = rb.rotation * clientRotError;

            // rewind & replay
            rb.position = serverSimulationState.position;
            rb.rotation = serverSimulationState.rotation;
            rb.velocity = serverSimulationState.velocity;
            rb.angularVelocity = serverSimulationState.angularVelocity;

            uint rewindTickNumber = serverSimulationState.currentTick;
            while (rewindTickNumber < cTick)
            {
                cacheIndex = rewindTickNumber % CacheSize;

                clientStateCache[cacheIndex] = CurrentSimulationState(rb);

                PhysicsStep(rb, inputCache[cacheIndex].Inputs);
                Physics.Simulate(deltaTickTime);

                ++rewindTickNumber;
            }

            // if more than 2ms apart, just snap
            if ((prevPos - rb.position).sqrMagnitude >= 4.0f)
            {
                clientPosError = Vector3.zero;
                clientRotError = Quaternion.identity;
            }
            else
            {
                clientPosError = prevPos - rb.position;
                clientRotError = Quaternion.Inverse(rb.rotation) * prevRot;
            }
        }
        lastCorrectedFrame = playerScript.serverSimulationState.currentTick;
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
        if (rigidbody.transform.position.y <= playerMovementImpulse && inputs[4])
        {
            rigidbody.AddForce(Vector3.up * playerMovementImpulse, ForceMode.Impulse);
        }
            
    }

    private SimulationState CurrentSimulationState(Rigidbody rb)
    {
        return new SimulationState
        {
            position = rb.position,
            rotation = rb.rotation,
            currentTick = cTick
        };
    }

    private ClientInput GetInput()
    {
        bool[] tempInputs = new bool[5];
        if (Input.GetKey(KeyCode.W))
            tempInputs[0] = true;

        if (Input.GetKey(KeyCode.S))
            tempInputs[1] = true;

        if (Input.GetKey(KeyCode.A))
            tempInputs[2] = true;

        if (Input.GetKey(KeyCode.D))
            tempInputs[3] = true;

        if (Input.GetKey(KeyCode.Space))
            tempInputs[4] = true;

        return new ClientInput
        {
            Inputs = tempInputs,
            currentTick = cTick
        };
    }

    #region Messages
    private void SendInput()
    {
        Message message = Message.Create(MessageSendMode.Unreliable, ClientToServerId.PlayerInput);

        message.AddByte((byte)(cTick - playerScript.serverSimulationState.currentTick));

        for (int i = playerScript.serverSimulationState.currentTick; i < cTick; i++)
        {
            message.AddBools(inputCache[i % CacheSize].Inputs, false);
            message.AddUShort(inputCache[i % CacheSize].currentTick);
        }
        NetworkManager.Singleton.Client.Send(message);
    }
    #endregion
}

